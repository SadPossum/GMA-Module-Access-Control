namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Permissions;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Handlers;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.ValueObjects;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessProfileMutationAdmissionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly AccessScope OwnerScope =
        AccessScope.Parse("tenant:tenant-a");
    private static readonly AccessSubject Actor =
        AccessSubject.User("owner-a");

    [Fact]
    public async Task No_product_policy_preserves_default_module_behavior()
    {
        AccessProfileMutationAdmissionPolicy admission = Admission();

        Result result = await admission.AuthorizeAsync(
            Context(AccessProfileMutationAdmissionOperation.CreateProfile),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(
        AccessProfileMutationAdmissionDecision.Denied,
        "AccessControl.ProfileMutationRejected")]
    [InlineData(
        AccessProfileMutationAdmissionDecision.Unavailable,
        "AccessControl.ProfileMutationAdmissionUnavailable")]
    [InlineData(
        AccessProfileMutationAdmissionDecision.Unknown,
        "AccessControl.ProfileMutationAdmissionUnavailable")]
    public async Task Product_decisions_fail_closed_with_stable_errors(
        AccessProfileMutationAdmissionDecision decision,
        string expectedErrorCode)
    {
        RecordingPolicy policy = new(decision);
        AccessProfileMutationAdmissionPolicy admission = Admission(policy);

        Result result = await admission.AuthorizeAsync(
            Context(AccessProfileMutationAdmissionOperation.CreateProfile),
            CancellationToken.None);

        Assert.Equal(expectedErrorCode, result.Error.Code);
        Assert.Single(policy.Contexts);
    }

    [Fact]
    public async Task Policy_failure_is_contained_and_fails_closed()
    {
        RecordingPolicy policy = new(
            AccessProfileMutationAdmissionDecision.Allowed)
        {
            Exception = new InvalidOperationException("offline")
        };
        AccessProfileMutationAdmissionPolicy admission = Admission(policy);

        Result result = await admission.AuthorizeAsync(
            Context(AccessProfileMutationAdmissionOperation.CreateProfile),
            CancellationToken.None);

        Assert.Equal(
            AccessControlApplicationErrors
                .ProfileMutationAdmissionUnavailable,
            result.Error);
    }

    [Fact]
    public async Task Create_denial_preserves_the_profile_store()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        RecordingPolicy policy = DenyingPolicy();
        CreateAccessProfileCommandHandler handler = new(
            new AccessProfileRepository(dbContext),
            CreatePermissionPolicy(),
            AccessControlTestAdmissions.AllowAll(),
            Admission(policy),
            new TestIds(),
            new TestClock());

        Result<AccessProfileDetails> result = await handler.HandleAsync(
            new CreateAccessProfileCommand(
                OwnerScope,
                "night-team",
                "Night team",
                null,
                ["reservations.read"],
                Actor),
            CancellationToken.None);

        Assert.Equal(
            AccessControlApplicationErrors.ProfileMutationRejected,
            result.Error);
        Assert.Empty(dbContext.AccessProfiles);
        AccessProfileMutationAdmissionContext context =
            Assert.Single(policy.Contexts);
        Assert.Equal(
            AccessProfileMutationAdmissionOperation.CreateProfile,
            context.Operation);
        Assert.Equal("night-team", context.ProfileKey);
        Assert.Null(context.ProfileId);
    }

    [Fact]
    public async Task Update_denial_preserves_profile_state()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessProfile profile = CreateProfile("front-desk");
        dbContext.AccessProfiles.Add(profile);
        await dbContext.SaveChangesAsync();
        RecordingPolicy policy = DenyingPolicy();
        UpdateAccessProfileCommandHandler handler = new(
            new AccessProfileRepository(dbContext),
            CreatePermissionPolicy(),
            AccessControlTestAdmissions.AllowAll(),
            Admission(policy),
            new TestIds(),
            new TestClock());

        Result<AccessProfileDetails> result = await handler.HandleAsync(
            new UpdateAccessProfileCommand(
                profile.Id,
                OwnerScope,
                "Changed",
                null,
                ["reservations.read"],
                profile.Version,
                Actor),
            CancellationToken.None);

        Assert.Equal(
            AccessControlApplicationErrors.ProfileMutationRejected,
            result.Error);
        Assert.Equal("Front desk", profile.DisplayName);
        Assert.Equal(1, profile.Version);
        AccessProfileMutationAdmissionContext context =
            Assert.Single(policy.Contexts);
        Assert.Equal(
            AccessProfileMutationAdmissionOperation.UpdateProfile,
            context.Operation);
        Assert.Equal(profile.Id, context.ProfileId);
        Assert.Equal(profile.Key, context.ProfileKey);
    }

    [Fact]
    public async Task Archive_denial_preserves_profile_state()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessProfile profile = CreateProfile("front-desk");
        dbContext.AccessProfiles.Add(profile);
        await dbContext.SaveChangesAsync();
        RecordingPolicy policy = DenyingPolicy();
        ArchiveAccessProfileCommandHandler handler = new(
            new AccessProfileRepository(dbContext),
            AccessControlTestAdmissions.AllowAll(),
            Admission(policy),
            new TestIds(),
            new TestClock());

        Result<Unit> result = await handler.HandleAsync(
            new ArchiveAccessProfileCommand(
                profile.Id,
                OwnerScope,
                profile.Version,
                Actor),
            CancellationToken.None);

        Assert.Equal(
            AccessControlApplicationErrors.ProfileMutationRejected,
            result.Error);
        Assert.Equal(
            Gma.Modules.AccessControl.Domain.Enums.AccessProfileStatus.Active,
            profile.Status);
        Assert.Equal(1, profile.Version);
        AccessProfileMutationAdmissionContext context =
            Assert.Single(policy.Contexts);
        Assert.Equal(
            AccessProfileMutationAdmissionOperation.ArchiveProfile,
            context.Operation);
    }

    [Fact]
    public async Task Ensure_replay_bypasses_policy_but_new_profile_is_admitted()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessProfile existing = CreateProfile("front-desk");
        dbContext.AccessProfiles.Add(existing);
        await dbContext.SaveChangesAsync();
        RecordingPolicy policy = DenyingPolicy();
        EnsureAccessProfileCommandHandler handler = new(
            new AccessProfileRepository(dbContext),
            CreatePermissionPolicy(),
            AccessControlTestAdmissions.AllowAll(),
            Admission(policy),
            new TestIds(),
            new TestClock());

        Result<AccessProfileDetails> replay = await handler.HandleAsync(
            new EnsureAccessProfileCommand(
                OwnerScope,
                Definition(existing.Key),
                Actor),
            CancellationToken.None);
        Result<AccessProfileDetails> denied = await handler.HandleAsync(
            new EnsureAccessProfileCommand(
                OwnerScope,
                Definition("housekeeping"),
                Actor),
            CancellationToken.None);

        Assert.True(replay.IsSuccess, replay.Error.Code);
        Assert.Equal(existing.Id, replay.Value.Id);
        Assert.Equal(
            AccessControlApplicationErrors.ProfileMutationRejected,
            denied.Error);
        Assert.Single(dbContext.AccessProfiles);
        AccessProfileMutationAdmissionContext context =
            Assert.Single(policy.Contexts);
        Assert.Equal(
            AccessProfileMutationAdmissionOperation.EnsureProfile,
            context.Operation);
        Assert.Equal("housekeeping", context.ProfileKey);
    }

    private static AccessProfileMutationAdmissionPolicy Admission(
        params IAccessProfileMutationAdmissionPolicy[] policies) =>
        new(
            policies,
            NullLogger<AccessProfileMutationAdmissionPolicy>.Instance);

    private static AccessProfileMutationAdmissionContext Context(
        AccessProfileMutationAdmissionOperation operation) =>
        new(operation, OwnerScope, Actor, ProfileKey: "front-desk");

    private static RecordingPolicy DenyingPolicy() =>
        new(AccessProfileMutationAdmissionDecision.Denied);

    private static AccessProfilePermissionPolicy CreatePermissionPolicy() =>
        new(
            [new AccessProfileAllowedPermission(
                PermissionCode.Create("reservations.read"))],
            new AllowingAuthorization());

    private static AccessControlDbContext CreateDbContext()
    {
        DbContextOptions<AccessControlDbContext> options =
            new DbContextOptionsBuilder<AccessControlDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
        return new AccessControlDbContext(options);
    }

    private static AccessProfile CreateProfile(string key)
    {
        Result<AccessProfile> result = AccessProfile.Create(
            Guid.NewGuid(),
            OwnerScope.Value,
            key,
            "Front desk",
            null,
            ["reservations.read"],
            new AccessProfileSubject(
                Gma.Modules.AccessControl.Domain.Enums
                    .AccessProfileSubjectKind.User,
                Actor.Id),
            Guid.NewGuid(),
            Now);
        Assert.True(result.IsSuccess, result.Error.Code);
        return result.Value;
    }

    private static AccessProfileDefinition Definition(string key) =>
        new(
            key,
            "Seed profile",
            null,
            ["reservations.read"]);

    private sealed class RecordingPolicy(
        AccessProfileMutationAdmissionDecision decision)
        : IAccessProfileMutationAdmissionPolicy
    {
        public List<AccessProfileMutationAdmissionContext> Contexts { get; } =
            [];
        public Exception? Exception { get; init; }

        public ValueTask<AccessProfileMutationAdmissionDecision> EvaluateAsync(
            AccessProfileMutationAdmissionContext context,
            CancellationToken cancellationToken = default)
        {
            this.Contexts.Add(context);
            return this.Exception is null
                ? ValueTask.FromResult(decision)
                : ValueTask.FromException<
                    AccessProfileMutationAdmissionDecision>(this.Exception);
        }
    }

    private sealed class AllowingAuthorization
        : IAccessAuthorizationService
    {
        public Task<AccessDecision> AuthorizeAsync(
            AccessRequirement requirement,
            CancellationToken cancellationToken) =>
            Task.FromResult(AccessDecision.Allowed());

        public Task<IReadOnlyList<AccessDecision>> AuthorizeManyAsync(
            IReadOnlyList<AccessRequirement> requirements,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AccessDecision>>(
                requirements
                    .Select(_ => AccessDecision.Allowed())
                    .ToArray());
    }

    private sealed class TestIds : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }

    private sealed class TestClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
