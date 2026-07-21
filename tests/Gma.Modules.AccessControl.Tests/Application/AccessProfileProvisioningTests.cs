namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Handlers;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.ValueObjects;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using DomainChangeKind = Gma.Modules.AccessControl.Domain.Enums.AccessProfileChangeKind;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessProfileProvisioningTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly AccessScope ScopeA = AccessScope.Parse("tenant:tenant-a");
    private static readonly AccessScope ScopeB = AccessScope.Parse("tenant:tenant-b");
    private static readonly AccessSubject Actor = AccessSubject.User("owner-a");
    private static readonly AccessSubject Subject = AccessSubject.User("member-a");

    [Fact]
    public async Task Ensure_returns_existing_profile_without_overwriting_customer_edits()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessProfile existing = CreateProfile(ScopeA, "front-desk", "Custom front desk");
        dbContext.AccessProfiles.Add(existing);
        await dbContext.SaveChangesAsync();
        AccessProfileRepository repository = new(dbContext);
        RecordingAuthorization authorization = new();
        EnsureAccessProfileCommandHandler handler = new(
            repository,
            CreatePermissionPolicy(authorization),
            new SequenceIdGenerator(),
            new FixedClock(Now.AddMinutes(1)));

        Result<AccessProfileDetails> result = await handler.HandleAsync(
            new EnsureAccessProfileCommand(
                ScopeA,
                new AccessProfileDefinition(
                    "front-desk",
                    "Seed name",
                    "Seed description",
                    ["reservations.read"]),
                Actor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Value.Id);
        Assert.Equal("Custom front desk", result.Value.DisplayName);
        Assert.Empty(authorization.Requirements);
        Assert.Single(dbContext.AccessProfiles);
    }

    [Fact]
    public async Task Reconcile_replaces_the_exact_scope_set_and_is_idempotent()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessProfile oldProfile = CreateProfile(ScopeA, "old", "Old");
        AccessProfile desiredProfile = CreateProfile(ScopeA, "desired", "Desired");
        AccessProfile otherScope = CreateProfile(ScopeB, "other", "Other");
        dbContext.AccessProfiles.AddRange(oldProfile, desiredProfile, otherScope);
        dbContext.AccessProfileAssignments.AddRange(
            CreateAssignment(oldProfile.Id, ScopeA, Subject),
            CreateAssignment(otherScope.Id, ScopeB, Subject));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        SequenceIdGenerator ids = new();
        AccessProfileRepository profiles = new(dbContext);
        AccessControlRbacRepository rbac = new(dbContext, ids, new ExactScopeMatchOptionsResolver());
        ReconcileAccessProfileAssignmentsCommandHandler handler = new(
            profiles,
            rbac,
            CreatePermissionPolicy(new RecordingAuthorization()),
            new AccessProfileAssignmentPolicy([]),
            ids,
            new FixedClock(Now.AddMinutes(1)));

        Result<AccessProfileAssignmentReconciliationDetails> result = await handler.HandleAsync(
            new ReconcileAccessProfileAssignmentsCommand(Subject, ScopeA, [desiredProfile.Id], Actor),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        Result<AccessProfileAssignmentReconciliationDetails> replay = await handler.HandleAsync(
            new ReconcileAccessProfileAssignmentsCommand(Subject, ScopeA, [desiredProfile.Id], Actor),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AssignedCount);
        Assert.Equal(1, result.Value.UnassignedCount);
        Assert.True(replay.IsSuccess);
        Assert.Equal(0, replay.Value.AssignedCount);
        Assert.Equal(0, replay.Value.UnassignedCount);
        Guid[] assignedProfileIds = await dbContext.AccessProfileAssignments
            .Where(assignment => assignment.SubjectId == Subject.Id)
            .OrderBy(assignment => assignment.ProfileId)
            .Select(assignment => assignment.ProfileId)
            .ToArrayAsync();
        Assert.Equal(new[] { desiredProfile.Id, otherScope.Id }.Order(), assignedProfileIds);
        Assert.Equal(2, await dbContext.AccessProfileChanges.CountAsync(change =>
            change.SubjectId == Subject.Id &&
            (change.Kind == DomainChangeKind.Assigned || change.Kind == DomainChangeKind.Unassigned)));
    }

    [Fact]
    public async Task Reconcile_rejects_the_complete_change_when_any_target_profile_is_ineligible()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessProfile existing = CreateProfile(ScopeA, "existing", "Existing");
        AccessProfile desired = CreateProfile(ScopeA, "desired", "Desired");
        dbContext.AccessProfiles.AddRange(existing, desired);
        dbContext.AccessProfileAssignments.Add(CreateAssignment(existing.Id, ScopeA, Subject));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        SequenceIdGenerator ids = new();
        AccessProfileRepository profiles = new(dbContext);
        ReconcileAccessProfileAssignmentsCommandHandler handler = new(
            profiles,
            new AccessControlRbacRepository(dbContext, ids, new ExactScopeMatchOptionsResolver()),
            CreatePermissionPolicy(new RecordingAuthorization()),
            new AccessProfileAssignmentPolicy([new DenyingAssignmentPolicy()]),
            ids,
            new FixedClock(Now.AddMinutes(1)));

        Result<AccessProfileAssignmentReconciliationDetails> result = await handler.HandleAsync(
            new ReconcileAccessProfileAssignmentsCommand(Subject, ScopeA, [desired.Id], Actor),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccessControlApplicationErrors.ProfileAssignmentRejected, result.Error);
        AccessProfileAssignment assignment = Assert.Single(dbContext.AccessProfileAssignments);
        Assert.Equal(existing.Id, assignment.ProfileId);
    }

    [Fact]
    public async Task Scoped_reconcile_is_idempotent_and_legacy_reconcile_leaves_descendant_targets_untouched()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessScope propertyA = AccessScope.Parse("tenant:tenant-a/property:property-a");
        AccessScope propertyB = AccessScope.Parse("tenant:tenant-a/property:property-b");
        AccessProfile profile = CreateProfile(ScopeA, "front-desk", "Front desk");
        dbContext.AccessProfiles.Add(profile);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        SequenceIdGenerator ids = new();
        AccessProfileRepository profiles = new(dbContext);
        AccessControlRbacRepository rbac = new(dbContext, ids, new ExactScopeMatchOptionsResolver());
        ReconcileScopedAccessProfileAssignmentsCommandHandler scopedHandler = new(
            profiles,
            rbac,
            CreatePermissionPolicy(new RecordingAuthorization()),
            new AccessProfileAssignmentPolicy([]),
            ids,
            new FixedClock(Now.AddMinutes(1)));
        AccessProfileAssignmentTarget[] targets =
        [
            new(profile.Id, propertyA),
            new(profile.Id, propertyB)
        ];

        Result<ScopedAccessProfileAssignmentReconciliationDetails> result = await scopedHandler.HandleAsync(
            new ReconcileScopedAccessProfileAssignmentsCommand(Subject, ScopeA, targets, Actor),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        Result<ScopedAccessProfileAssignmentReconciliationDetails> replay = await scopedHandler.HandleAsync(
            new ReconcileScopedAccessProfileAssignmentsCommand(Subject, ScopeA, targets, Actor),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.AssignedCount);
        Assert.Equal(0, result.Value.UnassignedCount);
        Assert.True(replay.IsSuccess);
        Assert.Equal(0, replay.Value.AssignedCount);
        Assert.Equal(0, replay.Value.UnassignedCount);

        ReconcileAccessProfileAssignmentsCommandHandler legacyHandler = new(
            profiles,
            rbac,
            CreatePermissionPolicy(new RecordingAuthorization()),
            new AccessProfileAssignmentPolicy([]),
            ids,
            new FixedClock(Now.AddMinutes(2)));
        Result<AccessProfileAssignmentReconciliationDetails> legacy = await legacyHandler.HandleAsync(
            new ReconcileAccessProfileAssignmentsCommand(Subject, ScopeA, [], Actor),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        Assert.True(legacy.IsSuccess);
        Assert.Equal(0, legacy.Value.UnassignedCount);
        Assert.Equal(2, await dbContext.AccessProfileAssignments.CountAsync());

        Result<ScopedAccessProfileAssignmentReconciliationDetails> narrowed = await scopedHandler.HandleAsync(
            new ReconcileScopedAccessProfileAssignmentsCommand(
                Subject,
                ScopeA,
                [new AccessProfileAssignmentTarget(profile.Id, propertyB)],
                Actor),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.True(narrowed.IsSuccess);
        Assert.Equal(0, narrowed.Value.AssignedCount);
        Assert.Equal(1, narrowed.Value.UnassignedCount);
        AccessProfileAssignment remaining = Assert.Single(dbContext.AccessProfileAssignments);
        Assert.Equal(propertyB.Value, remaining.AssignmentScopeValue);
        string[] historyScopes = await dbContext.AccessProfileChanges
            .Where(change => change.SubjectId == Subject.Id)
            .OrderBy(change => change.OccurredAtUtc)
            .ThenBy(change => change.Id)
            .Select(change => change.AssignmentScopeValue!)
            .ToArrayAsync();
        Assert.Equal(3, historyScopes.Length);
        string[] expectedScopes = [propertyA.Value, propertyB.Value];
        Assert.All(historyScopes, scope => Assert.Contains(scope, expectedScopes));
    }

    [Fact]
    public async Task Scoped_reconcile_rejects_unrelated_assignment_scope_without_writing()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessProfile profile = CreateProfile(ScopeA, "front-desk", "Front desk");
        dbContext.AccessProfiles.Add(profile);
        await dbContext.SaveChangesAsync();
        SequenceIdGenerator ids = new();
        AccessProfileRepository profiles = new(dbContext);
        ReconcileScopedAccessProfileAssignmentsCommandHandler handler = new(
            profiles,
            new AccessControlRbacRepository(dbContext, ids, new ExactScopeMatchOptionsResolver()),
            CreatePermissionPolicy(new RecordingAuthorization()),
            new AccessProfileAssignmentPolicy([]),
            ids,
            new FixedClock(Now.AddMinutes(1)));

        Result<ScopedAccessProfileAssignmentReconciliationDetails> result = await handler.HandleAsync(
            new ReconcileScopedAccessProfileAssignmentsCommand(
                Subject,
                ScopeA,
                [new AccessProfileAssignmentTarget(profile.Id, ScopeB)],
                Actor),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccessControlApplicationErrors.ProfileAssignmentScopeInvalid, result.Error);
        Assert.Empty(dbContext.AccessProfileAssignments);
    }

    private static AccessControlDbContext CreateDbContext()
    {
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AccessControlDbContext(options);
    }

    private static AccessProfile CreateProfile(AccessScope scope, string key, string displayName)
    {
        Result<AccessProfile> result = AccessProfile.Create(
            Guid.NewGuid(),
            scope.Value,
            key,
            displayName,
            null,
            ["reservations.read"],
            ToDomain(Actor),
            Guid.NewGuid(),
            Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AccessProfileAssignment CreateAssignment(
        Guid profileId,
        AccessScope assignmentScope,
        AccessSubject subject)
    {
        Result<AccessProfileAssignment> result = AccessProfileAssignment.Create(
            Guid.NewGuid(), profileId, assignmentScope.Value, ToDomain(subject), ToDomain(Actor), Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AccessProfilePermissionPolicy CreatePermissionPolicy(
        IAccessAuthorizationService authorization) =>
        new(
            [new AccessProfileAllowedPermission(PermissionCode.Create("reservations.read"))],
            authorization);

    private static AccessProfileSubject ToDomain(AccessSubject subject) =>
        new((AccessProfileSubjectKind)subject.Kind, subject.Id);

    private sealed class RecordingAuthorization : IAccessAuthorizationService
    {
        public List<AccessRequirement> Requirements { get; } = [];

        public Task<AccessDecision> AuthorizeAsync(
            AccessRequirement requirement,
            CancellationToken cancellationToken)
        {
            this.Requirements.Add(requirement);
            return Task.FromResult(AccessDecision.Allowed());
        }

        public Task<IReadOnlyList<AccessDecision>> AuthorizeManyAsync(
            IReadOnlyList<AccessRequirement> requirements,
            CancellationToken cancellationToken)
        {
            this.Requirements.AddRange(requirements);
            return Task.FromResult<IReadOnlyList<AccessDecision>>(
                requirements.Select(_ => AccessDecision.Allowed()).ToArray());
        }
    }

    private sealed class DenyingAssignmentPolicy : IAccessProfileAssignmentPolicy
    {
        public ValueTask<bool> IsAllowedAsync(
            AccessProfileAssignmentPolicyContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
    }

    private sealed class ExactScopeMatchOptionsResolver : IAccessScopeMatchOptionsResolver
    {
        public AccessScopeMatchOptions Resolve(PermissionCode permission) => new();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }
}
