namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Handlers;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.ValueObjects;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessProfilePermissionPolicyTests
{
    private static readonly AccessSubject Actor = AccessSubject.User("actor-a");
    private static readonly AccessScope Scope = AccessScope.Parse("tenant:tenant-a");

    [Fact]
    public async Task Empty_allowlist_denies_without_authorizing_the_actor()
    {
        StubAuthorizationService authorization = new(_ => AccessDecision.Allowed());
        AccessProfilePermissionPolicy policy = new([], authorization);

        Result result = await policy.ValidateDelegationAsync(
            Actor, Scope, ["reservations.read"], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccessControlApplicationErrors.ProfilePermissionNotAllowed, result.Error);
        Assert.Empty(authorization.Requirements);
    }

    [Fact]
    public async Task Allowlisted_permission_still_requires_the_actor_to_hold_it_in_the_owner_scope()
    {
        StubAuthorizationService authorization = new(_ =>
            AccessDecision.Denied("AccessControl.TestDenied"));
        AccessProfilePermissionPolicy policy = CreatePolicy(authorization, "reservations.read");

        Result result = await policy.ValidateDelegationAsync(
            Actor, Scope, ["reservations.read"], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccessControlApplicationErrors.ProfilePermissionEscalation, result.Error);
        AccessRequirement requirement = Assert.Single(authorization.Requirements);
        Assert.Equal(Actor, requirement.Subject);
        Assert.Equal("reservations.read", requirement.Permission.Value);
        Assert.Equal(Scope, requirement.Scope);
    }

    [Fact]
    public async Task Actor_may_delegate_only_allowlisted_permissions_they_hold()
    {
        StubAuthorizationService authorization = new(_ => AccessDecision.Allowed());
        AccessProfilePermissionPolicy policy = CreatePolicy(
            authorization, "reservations.read", "guests.read");

        Result result = await policy.ValidateDelegationAsync(
            Actor,
            Scope,
            ["reservations.read", "guests.read", "reservations.read"],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, authorization.Requirements.Count);
        Assert.Equal(
            ["reservations.read", "guests.read"],
            authorization.Requirements.Select(requirement => requirement.Permission.Value));
    }

    [Fact]
    public async Task Assigning_an_existing_profile_rechecks_actor_delegation_rights()
    {
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using AccessControlDbContext dbContext = new(options);
        AccessProfileRepository repository = new(dbContext);
        Result<AccessProfile> created = AccessProfile.Create(
            Guid.NewGuid(), Scope.Value, "front-desk", "Front desk", null,
            ["reservations.read"], ToDomain(Actor), Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.True(created.IsSuccess);
        repository.Add(created.Value);
        await dbContext.SaveChangesAsync();
        StubAuthorizationService authorization = new(_ =>
            AccessDecision.Denied("AccessControl.TestDenied"));
        AccessProfilePermissionPolicy policy = CreatePolicy(authorization, "reservations.read");
        AssignAccessProfileCommandHandler handler = new(
            repository,
            rbac: null!,
            policy,
            ids: null!,
            clock: null!);

        Result<AccessProfileAssignmentDetails> result = await handler.HandleAsync(
            new AssignAccessProfileCommand(
                created.Value.Id,
                Scope,
                AccessSubject.User("subject-a"),
                Actor),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccessControlApplicationErrors.ProfilePermissionEscalation, result.Error);
        Assert.Empty(dbContext.AccessProfileAssignments);
    }

    private static AccessProfileSubject ToDomain(AccessSubject subject) =>
        new(
            subject.Kind switch
            {
                AccessSubjectKind.User => AccessProfileSubjectKind.User,
                AccessSubjectKind.AdminActor => AccessProfileSubjectKind.AdminActor,
                AccessSubjectKind.Service => AccessProfileSubjectKind.Service,
                AccessSubjectKind.System => AccessProfileSubjectKind.System,
                _ => throw new ArgumentOutOfRangeException(nameof(subject))
            },
            subject.Id);

    private static AccessProfilePermissionPolicy CreatePolicy(
        IAccessAuthorizationService authorization,
        params string[] permissions) =>
        new(
            permissions.Select(permission =>
                new AccessProfileAllowedPermission(PermissionCode.Create(permission))),
            authorization);

    private sealed class StubAuthorizationService(Func<AccessRequirement, AccessDecision> decide)
        : IAccessAuthorizationService
    {
        public List<AccessRequirement> Requirements { get; } = [];

        public Task<AccessDecision> AuthorizeAsync(
            AccessRequirement requirement,
            CancellationToken cancellationToken)
        {
            this.Requirements.Add(requirement);
            return Task.FromResult(decide(requirement));
        }
    }
}
