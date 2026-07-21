namespace Gma.Modules.AccessControl.Tests.Application;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Contracts;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ScopedAccessProfileManagerTests
{
    private static readonly AccessScope OwnerScope = AccessScope.Create(
        AccessScopeSegment.Create("tenant", "workspace-a"));
    private static readonly AccessSubject Actor = AccessSubject.User("manager-a");
    private static readonly AccessSubject Member = AccessSubject.User("member-a");

    [Fact]
    public async Task Read_contract_authorizes_and_returns_scoped_assignments()
    {
        ScopedAccessProfileAssignmentSet expected = new(Member, OwnerScope, []);
        StubProvisioner provisioner = new(expected);
        RecordingAuthorization authorization = new();
        ScopedAccessProfileManager manager = new(
            provisioner,
            new RecordingDispatcher(_ => throw new InvalidOperationException()),
            authorization);

        Result<ScopedAccessProfileAssignmentSet> result = await manager.GetSubjectAssignmentsAsync(
            Member,
            OwnerScope,
            Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
        AccessRequirement requirement = Assert.Single(authorization.Requirements);
        Assert.Equal(AccessControlProfilePermissionCodes.Read, requirement.Permission.Value);
        Assert.Equal(OwnerScope, requirement.Scope);
    }

    [Fact]
    public async Task Reconcile_contract_authorizes_assign_and_preserves_actor()
    {
        AccessProfileAssignmentTarget target = new(Guid.NewGuid(), OwnerScope);
        RecordingDispatcher dispatcher = new(request => request switch
        {
            ReconcileScopedAccessProfileAssignmentsCommand command => Result.Success(
                new ScopedAccessProfileAssignmentReconciliationDetails(
                    command.Subject,
                    command.OwnerScope,
                    command.Targets.ToArray(),
                    1,
                    0)),
            _ => throw new InvalidOperationException()
        });
        RecordingAuthorization authorization = new();
        ScopedAccessProfileManager manager = new(
            new StubProvisioner(new(Member, OwnerScope, [])),
            dispatcher,
            authorization);

        Result<ScopedAccessProfileAssignmentReconciliation> result = await manager
            .ReconcileSubjectAssignmentsAsync(Member, OwnerScope, [target], Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal([target], result.Value.Targets);
        ReconcileScopedAccessProfileAssignmentsCommand command = Assert.IsType<
            ReconcileScopedAccessProfileAssignmentsCommand>(Assert.Single(dispatcher.Requests));
        Assert.Equal(Actor, command.Actor);
        Assert.Equal(
            AccessControlProfilePermissionCodes.Assign,
            Assert.Single(authorization.Requirements).Permission.Value);
    }

    [Theory]
    [InlineData("AccessControl.ProfileAssignmentRejected", "AccessControl.ScopedProfileAssignmentRejected")]
    [InlineData("AccessControl.ProfilePermissionEscalation", "AccessControl.ScopedProfilePermissionEscalation")]
    [InlineData("AccessControl.ProfileAssignmentScopeInvalid", "AccessControl.ScopedProfileAssignmentScopeInvalid")]
    [InlineData("AccessControl.ProfileNotFound", "AccessControl.ScopedProfileUnavailable")]
    public async Task Reconcile_maps_internal_failures_to_contract_errors(
        string sourceCode,
        string expectedCode)
    {
        RecordingDispatcher dispatcher = new(_ =>
            Result.Failure<ScopedAccessProfileAssignmentReconciliationDetails>(
                new Error(sourceCode, "Internal detail.")));
        ScopedAccessProfileManager manager = new(
            new StubProvisioner(new(Member, OwnerScope, [])),
            dispatcher,
            new RecordingAuthorization());

        Result<ScopedAccessProfileAssignmentReconciliation> result = await manager
            .ReconcileSubjectAssignmentsAsync(Member, OwnerScope, [], Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public async Task Denied_requests_stop_before_read_or_dispatch()
    {
        StubProvisioner provisioner = new(new(Member, OwnerScope, []));
        RecordingDispatcher dispatcher = new(_ =>
            throw new InvalidOperationException("Denied requests must not dispatch."));
        ScopedAccessProfileManager manager = new(
            provisioner,
            dispatcher,
            new RecordingAuthorization(allowed: false));

        Result<ScopedAccessProfileAssignmentSet> read = await manager.GetSubjectAssignmentsAsync(
            Member,
            OwnerScope,
            Actor);
        Result<ScopedAccessProfileAssignmentReconciliation> write = await manager
            .ReconcileSubjectAssignmentsAsync(Member, OwnerScope, [], Actor);

        Assert.Equal(ScopedAccessProfileManagementErrors.AccessDenied, read.Error);
        Assert.Equal(ScopedAccessProfileManagementErrors.AccessDenied, write.Error);
        Assert.Equal(0, provisioner.ReadCount);
        Assert.Empty(dispatcher.Requests);
    }

    private sealed class StubProvisioner(ScopedAccessProfileAssignmentSet assignments)
        : IScopedAccessProfileProvisioner
    {
        public int ReadCount { get; private set; }

        public Task<ScopedAccessProfileAssignmentSet> GetSubjectScopedAssignmentsAsync(
            AccessSubject subject,
            AccessScope ownerScope,
            CancellationToken cancellationToken = default)
        {
            this.ReadCount++;
            return Task.FromResult(assignments);
        }

        public Task<ScopedAccessProfileAssignmentReconciliation> ReconcileSubjectScopedAssignmentsAsync(
            AccessSubject subject,
            AccessScope ownerScope,
            IReadOnlyCollection<AccessProfileAssignmentTarget> targets,
            AccessSubject actor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingDispatcher(Func<object, object> dispatch) : IRequestDispatcher
    {
        public List<object> Requests { get; } = [];

        public Task<Result<TResponse>> SendAsync<TResponse>(
            ICommand<TResponse> command,
            CancellationToken cancellationToken = default) => this.Dispatch<TResponse>(command);

        public Task<Result<TResponse>> QueryAsync<TResponse>(
            IQuery<TResponse> query,
            CancellationToken cancellationToken = default) => this.Dispatch<TResponse>(query);

        private Task<Result<TResponse>> Dispatch<TResponse>(object request)
        {
            this.Requests.Add(request);
            return Task.FromResult((Result<TResponse>)dispatch(request));
        }
    }

    private sealed class RecordingAuthorization(bool allowed = true) : IAccessAuthorizationService
    {
        public List<AccessRequirement> Requirements { get; } = [];

        public Task<AccessDecision> AuthorizeAsync(
            AccessRequirement requirement,
            CancellationToken cancellationToken)
        {
            this.Requirements.Add(requirement);
            return Task.FromResult(allowed
                ? AccessDecision.Allowed()
                : AccessDecision.Denied("test.denied"));
        }
    }
}
