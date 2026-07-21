namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Queries;
using Gma.Modules.AccessControl.Contracts;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessProfileManagementTests
{
    private static readonly AccessScope OwnerScope = AccessScope.Parse("tenant:tenant-a");
    private static readonly AccessSubject Actor = AccessSubject.User("owner-a");

    [Fact]
    public async Task Read_contract_maps_application_details_and_preserves_page_metadata()
    {
        Guid profileId = Guid.NewGuid();
        AccessProfileDetails details = CreateDetails(profileId);
        RecordingDispatcher dispatcher = new(request => request switch
        {
            ListAccessProfilesQuery => Result.Success(new AccessControlPage<AccessProfileDetails>(
                [details], 2, 5, true)),
            ListAllowedAccessProfilePermissionsQuery =>
                Result.Success<IReadOnlyList<string>>(["reservations.read", "staff.read"]),
            GetAccessProfileQuery => Result.Success(details),
            _ => throw new InvalidOperationException($"Unexpected request {request.GetType().Name}.")
        });
        RecordingAuthorization authorization = new();
        AccessProfileManager manager = new(dispatcher, authorization);

        Result<AccessControlPage<AccessProfileDto>> page = await manager.ListProfilesAsync(
            OwnerScope, includeArchived: true, page: 2, pageSize: 5, Actor);
        Result<IReadOnlyList<string>> permissions = await manager.ListAllowedPermissionsAsync(
            OwnerScope, Actor);
        Result<AccessProfileDto> profile = await manager.GetProfileAsync(profileId, OwnerScope, Actor);

        Assert.True(page.IsSuccess);
        Assert.Equal(2, page.Value.Page);
        Assert.Equal(5, page.Value.PageSize);
        Assert.True(page.Value.HasMore);
        Assert.Equal(profileId, Assert.Single(page.Value.Items).Id);
        Assert.Equal(["reservations.read", "staff.read"], permissions.Value);
        Assert.Equal(details.Permissions, profile.Value.Permissions);
        Assert.Collection(
            dispatcher.Requests,
            request => Assert.IsType<ListAccessProfilesQuery>(request),
            request => Assert.IsType<ListAllowedAccessProfilePermissionsQuery>(request),
            request => Assert.IsType<GetAccessProfileQuery>(request));
        Assert.All(authorization.Requirements, requirement =>
        {
            Assert.Equal(Actor, requirement.Subject);
            Assert.Equal(OwnerScope, requirement.Scope);
            Assert.Equal(AccessControlProfilePermissionCodes.Read, requirement.Permission.Value);
        });
    }

    [Fact]
    public async Task Mutation_contract_dispatches_actor_and_preserves_expected_failures()
    {
        Guid profileId = Guid.NewGuid();
        Error conflict = new("AccessControl.ProfileVersionConflict", "The profile changed.");
        RecordingDispatcher dispatcher = new(request => request switch
        {
            CreateAccessProfileCommand command => Result.Success(CreateDetails(profileId) with
            {
                Key = command.Key,
                DisplayName = command.DisplayName,
                Description = command.Description ?? string.Empty,
                Permissions = command.Permissions.Order(StringComparer.Ordinal).ToArray()
            }),
            UpdateAccessProfileCommand => Result.Failure<AccessProfileDetails>(conflict),
            ArchiveAccessProfileCommand => Result.Failure<Unit>(conflict),
            _ => throw new InvalidOperationException($"Unexpected request {request.GetType().Name}.")
        });
        RecordingAuthorization authorization = new();
        AccessProfileManager manager = new(dispatcher, authorization);

        Result<AccessProfileDto> created = await manager.CreateProfileAsync(
            OwnerScope,
            new AccessProfileDefinition("night-team", "Night team", "Overnight staff", ["staff.read"]),
            Actor);
        Result<AccessProfileDto> updated = await manager.UpdateProfileAsync(
            profileId,
            OwnerScope,
            new AccessProfileUpdate("Night team", null, ["staff.read"], 4),
            Actor);
        Result archived = await manager.ArchiveProfileAsync(profileId, OwnerScope, 4, Actor);

        Assert.True(created.IsSuccess);
        Assert.Equal("night-team", created.Value.Key);
        Assert.Equal(conflict, updated.Error);
        Assert.Equal(conflict, archived.Error);
        CreateAccessProfileCommand create = Assert.IsType<CreateAccessProfileCommand>(dispatcher.Requests[0]);
        UpdateAccessProfileCommand update = Assert.IsType<UpdateAccessProfileCommand>(dispatcher.Requests[1]);
        ArchiveAccessProfileCommand archive = Assert.IsType<ArchiveAccessProfileCommand>(dispatcher.Requests[2]);
        Assert.Equal(Actor, create.Actor);
        Assert.Equal(Actor, update.Actor);
        Assert.Equal(Actor, archive.Actor);
        Assert.Equal(4, update.ExpectedVersion);
        Assert.Equal(4, archive.ExpectedVersion);
        Assert.All(authorization.Requirements, requirement =>
            Assert.Equal(AccessControlProfilePermissionCodes.Manage, requirement.Permission.Value));
    }

    [Fact]
    public async Task Management_contract_rejects_global_scopes_and_empty_profile_ids()
    {
        AccessProfileManager manager = new(new RecordingDispatcher(_ =>
            throw new InvalidOperationException("The request must not be dispatched.")),
            new RecordingAuthorization());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.ListProfilesAsync(AccessScope.Global, false, 1, 25, Actor));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.GetProfileAsync(Guid.Empty, OwnerScope, Actor));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.ArchiveProfileAsync(Guid.Empty, OwnerScope, 1, Actor));
    }

    [Fact]
    public async Task Management_contract_denies_before_dispatch()
    {
        RecordingDispatcher dispatcher = new(_ =>
            throw new InvalidOperationException("Denied requests must not be dispatched."));
        AccessProfileManager manager = new(dispatcher, new RecordingAuthorization(allowed: false));

        Result<AccessControlPage<AccessProfileDto>> result = await manager.ListProfilesAsync(
            OwnerScope, false, 1, 25, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(AccessProfileManagementErrors.AccessDenied, result.Error);
        Assert.Empty(dispatcher.Requests);
    }

    private static AccessProfileDetails CreateDetails(Guid profileId) =>
        new(
            profileId,
            OwnerScope,
            "front-desk",
            "Front desk",
            "Daily operations",
            AccessProfileStatus.Active,
            3,
            ["reservations.read"],
            7,
            new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero));

    private sealed class RecordingDispatcher(Func<object, object> dispatch) : IRequestDispatcher
    {
        public List<object> Requests { get; } = [];

        public Task<Result<TResponse>> SendAsync<TResponse>(
            ICommand<TResponse> command,
            CancellationToken cancellationToken = default) =>
            this.Dispatch<TResponse>(command);

        public Task<Result<TResponse>> QueryAsync<TResponse>(
            IQuery<TResponse> query,
            CancellationToken cancellationToken = default) =>
            this.Dispatch<TResponse>(query);

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
