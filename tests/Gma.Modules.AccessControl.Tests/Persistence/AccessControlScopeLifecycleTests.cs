namespace Gma.Modules.AccessControl.Tests.Persistence;

using Gma.Framework.AccessControl;
using Gma.Framework.Messaging;
using Gma.Framework.Messaging.Infrastructure;
using Gma.Framework.Permissions;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.ValueObjects;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DomainChangeKind =
    Gma.Modules.AccessControl.Domain.Enums.AccessProfileChangeKind;

[Trait("Category", "Unit")]
public sealed class AccessControlScopeLifecycleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly AccessScope TenantScope =
        AccessScope.Parse("tenant:tenant-a");
    private static readonly AccessScope SelectedScope =
        AccessScope.Parse("tenant:tenant-a/property:property-a");
    private static readonly AccessScope SelectedRoomScope =
        AccessScope.Parse(
            "tenant:tenant-a/property:property-a/room:room-a");
    private static readonly AccessScope SiblingScope =
        AccessScope.Parse("tenant:tenant-a/property:property-b");
    private static readonly AccessControlScopeCoordinate Coordinate =
        new(SelectedScope, "tenant-a-property-a");

    [Fact]
    public async Task Export_includes_owned_descendant_and_cross_owned_rows()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext dbContext =
            await CreateDbContextAsync(connection);
        SeededScope seeded = await SeedAsync(dbContext);
        AccessControlScopeLifecycleService lifecycle = CreateLifecycle(dbContext);

        AccessControlScopeSnapshot snapshot = await lifecycle.GetSnapshotAsync(
            Coordinate,
            CancellationToken.None);

        Assert.Equal(AccessControlScopeStatus.Open, snapshot.Status);
        Assert.Equal(0, snapshot.Revision);

        AccessControlRoleAssignmentExportRecord[] roleAssignments =
            await ExportAllAsync<AccessControlRoleAssignmentExportRecord>(
                lifecycle,
                snapshot.Revision,
                AccessControlScopeExportStore.RoleAssignments);
        Assert.Equal(2, roleAssignments.Length);
        Assert.All(roleAssignments, assignment =>
            Assert.StartsWith(SelectedScope.Value, assignment.AccessScopeValue));
        Assert.All(roleAssignments, assignment =>
            Assert.Equal(["properties.read"], assignment.RolePermissions));

        AccessControlProfileExportRecord profile = Assert.Single(
            await ExportAllAsync<AccessControlProfileExportRecord>(
                lifecycle,
                snapshot.Revision,
                AccessControlScopeExportStore.Profiles));
        Assert.Equal(seeded.OwnedProfileId, profile.ProfileId);

        AccessControlProfileAssignmentExportRecord[] profileAssignments =
            await ExportAllAsync<AccessControlProfileAssignmentExportRecord>(
                lifecycle,
                snapshot.Revision,
                AccessControlScopeExportStore.ProfileAssignments);
        Assert.Equal(2, profileAssignments.Length);
        Assert.Contains(profileAssignments, assignment =>
            assignment.ProfileId == seeded.AncestorProfileId &&
            assignment.AssignmentScopeValue == SelectedScope.Value);

        AccessControlProfileChangeExportRecord[] changes =
            await ExportAllAsync<AccessControlProfileChangeExportRecord>(
                lifecycle,
                snapshot.Revision,
                AccessControlScopeExportStore.ProfileChanges);
        Assert.Contains(changes, change =>
            change.ProfileId == seeded.AncestorProfileId &&
            change.AssignmentScopeValue == SelectedScope.Value);
        Assert.DoesNotContain(changes, change =>
            change.ProfileId == seeded.AncestorProfileId &&
            change.AssignmentScopeValue is null);
    }

    [Fact]
    public async Task Export_rejects_a_revision_crossed_by_another_management_write()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext dbContext =
            await CreateDbContextAsync(connection);
        await SeedAsync(dbContext);
        AccessControlScopeLifecycleService lifecycle = CreateLifecycle(dbContext);
        AccessControlScopeSnapshot snapshot = await lifecycle.GetSnapshotAsync(
            Coordinate,
            CancellationToken.None);

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync();
        await AccessControlManagementLock.AcquireAsync(
            dbContext,
            CancellationToken.None);
        await transaction.CommitAsync();

        AccessControlScopeExportPage page = await lifecycle.ExportAsync(
            new AccessControlScopeExportRequest(
                Coordinate,
                snapshot.Revision,
                AccessControlScopeExportStore.RoleAssignments,
                null,
                10),
            CancellationToken.None);

        Assert.Equal(AccessControlScopeExportStatus.Stale, page.Status);
        Assert.Equal(1, page.ScopeRevision);
        Assert.Empty(page.Records);
    }

    [Fact]
    public async Task Destruction_is_bounded_complete_and_principal_safe()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext dbContext =
            await CreateDbContextAsync(connection);
        SeededScope seeded = await SeedAsync(dbContext);
        AccessControlScopeLifecycleService lifecycle = CreateLifecycle(dbContext);
        AccessControlScopeSnapshot selected = await lifecycle.GetSnapshotAsync(
            Coordinate,
            CancellationToken.None);
        Guid operationId = Id(900);
        AccessControlScopeDestroyRequest request = new(
            operationId,
            Coordinate,
            selected.Revision,
            BatchSize: 1);

        AccessControlScopeDestroyResult result;
        int calls = 0;
        do
        {
            result = await lifecycle.DestroyBatchAsync(
                request,
                CancellationToken.None);
            calls++;
            Assert.True(calls < 30, "Destruction did not converge.");
        }
        while (result.Status == AccessControlScopeDestroyStatus.InProgress);

        Assert.Equal(AccessControlScopeDestroyStatus.Completed, result.Status);
        Assert.NotNull(result.Receipt);
        Assert.True(result.Receipt.RemovedRecordCount > 0);
        Assert.True(result.Receipt.CompletedBatchCount > 1);
        Assert.Empty(await dbContext.SubjectRoleAssignments
            .Where(assignment =>
                assignment.ScopeValue == SelectedScope.Value ||
                assignment.ScopeValue.StartsWith(SelectedScope.Value + "/"))
            .ToArrayAsync());
        Assert.Null(await dbContext.AccessProfiles.FindAsync(
            seeded.OwnedProfileId));
        Assert.NotNull(await dbContext.AccessProfiles.FindAsync(
            seeded.AncestorProfileId));
        Assert.DoesNotContain(await dbContext.AccessProfileAssignments
            .ToArrayAsync(), assignment =>
            assignment.AssignmentScopeValue == SelectedScope.Value ||
            assignment.AssignmentScopeValue.StartsWith(
                SelectedScope.Value + "/",
                StringComparison.Ordinal));
        Assert.DoesNotContain(await dbContext.AccessProfileChanges
            .ToArrayAsync(), change =>
            change.AssignmentScopeValue == SelectedScope.Value ||
            (change.AssignmentScopeValue?.StartsWith(
                SelectedScope.Value + "/",
                StringComparison.Ordinal) ?? false));
        Assert.False(await dbContext.Principals.AnyAsync(principal =>
            principal.Kind == (int)AccessSubjectKind.User &&
            principal.SubjectId == "selected-only"));
        Assert.True(await dbContext.Principals.AnyAsync(principal =>
            principal.Kind == (int)AccessSubjectKind.User &&
            principal.SubjectId == "shared"));
        Assert.True(await dbContext.Roles.AnyAsync(role =>
            role.Id == seeded.RoleId));
        Assert.True(await dbContext.RolePermissions.AnyAsync(permission =>
            permission.RoleId == seeded.RoleId));

        AccessControlScopeSnapshot closed = await lifecycle.GetSnapshotAsync(
            Coordinate,
            CancellationToken.None);
        Assert.Equal(AccessControlScopeStatus.Closed, closed.Status);
        Assert.Equal(result.Receipt.ResultingRevision, closed.Revision);
        Assert.Equal(selected.Revision, closed.SelectedRevision);

        AccessControlScopeDestroyResult replay =
            await lifecycle.DestroyBatchAsync(request, CancellationToken.None);
        AccessControlScopeDestroyResult conflict =
            await lifecycle.DestroyBatchAsync(
                request with { OperationId = Id(901) },
                CancellationToken.None);
        Assert.Equal(AccessControlScopeDestroyStatus.Replayed, replay.Status);
        Assert.Equal(AccessControlScopeDestroyStatus.Conflict, conflict.Status);
    }

    [Fact]
    public async Task Closed_scope_allows_reduction_but_denies_access_growth()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext dbContext =
            await CreateDbContextAsync(connection);
        SeededScope seeded = await SeedAsync(dbContext);
        AccessControlScopeState state = AccessControlScopeState.Create(
            SelectedScope.Value,
            Coordinate.TransportScopeId);
        Assert.Equal(
            AccessControlScopeCloseTransition.Completed,
            state.Close(Id(950), new string('a', 64), 1, Now));
        dbContext.AccessScopeStates.Add(state);
        await dbContext.SaveChangesAsync();

        AccessControlScopeAdmissionPolicy policy = new(dbContext);
        AccessSubject newSubject = AccessSubject.User("new-user");
        Assert.False(await policy.AreOpenAsync(
            [SiblingScope, SelectedScope, SelectedRoomScope],
            CancellationToken.None));
        Assert.True(await policy.AreOpenAsync(
            [SiblingScope],
            CancellationToken.None));

        dbContext.SubjectRoleAssignments.Add(
            new AccessSubjectRoleAssignment(
                Id(951),
                newSubject,
                seeded.RoleId,
                SelectedRoomScope,
                Now));
        await Assert.ThrowsAsync<AccessControlScopeClosedException>(() =>
            dbContext.SaveChangesAsync());
        dbContext.ChangeTracker.Clear();

        AccessSubjectRoleAssignment existing = await dbContext
            .SubjectRoleAssignments.SingleAsync(
            assignment => assignment.SubjectId == "selected-only");
        existing.Revoke(Now.AddMinutes(1));
        await dbContext.SaveChangesAsync();
        Assert.NotNull(existing.RevokedAtUtc);

        AccessProfileAssignment profileAssignment = await dbContext
            .AccessProfileAssignments
            .Include(assignment => assignment.Profile)
            .SingleAsync(assignment =>
                assignment.ProfileId == seeded.OwnedProfileId);
        profileAssignment.Profile!.RecordAssignmentChange(
            Id(952),
            DomainChangeKind.Unassigned,
            new AccessProfileSubject(
                AccessProfileSubjectKind.AdminActor,
                "operator"),
            ToDomain(AccessSubject.User(profileAssignment.SubjectId)),
            Now.AddMinutes(2),
            profileAssignment.AssignmentScopeValue);
        dbContext.AccessProfileAssignments.Remove(profileAssignment);
        await dbContext.SaveChangesAsync();
        Assert.False(await dbContext.AccessProfileAssignments.AnyAsync(
            assignment => assignment.Id == profileAssignment.Id));
    }

    [Fact]
    public async Task Active_inbox_is_busy_and_late_message_is_suppressed()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext dbContext =
            await CreateDbContextAsync(connection);
        InboxMessage active = InboxMessage.Create(
            Id(980),
            "handler-a",
            "gma.access-control.test.v1",
            "access-control-test",
            1,
            Coordinate.TransportScopeId,
            Now,
            Now);
        active.MarkProcessing("worker-a", Now);
        dbContext.InboxMessages.Add(active);
        await dbContext.SaveChangesAsync();
        AccessControlScopeLifecycleService lifecycle = CreateLifecycle(dbContext);

        AccessControlScopeDestroyResult busy = await lifecycle.DestroyBatchAsync(
            new AccessControlScopeDestroyRequest(
                Id(981),
                Coordinate,
                ExpectedRevision: 0,
                BatchSize: 10),
            CancellationToken.None);
        Assert.Equal(AccessControlScopeDestroyStatus.Busy, busy.Status);
        Assert.Empty(await dbContext.AccessScopeStates.ToArrayAsync());

        active.MarkProcessed(Now.AddMinutes(1));
        await dbContext.SaveChangesAsync();
        AccessControlScopeDestroyRequest request = new(
            Id(982),
            Coordinate,
            ExpectedRevision: 0,
            BatchSize: 10);
        AccessControlScopeDestroyResult closed;
        do
        {
            closed = await lifecycle.DestroyBatchAsync(
                request,
                CancellationToken.None);
        }
        while (closed.Status == AccessControlScopeDestroyStatus.InProgress);
        Assert.Equal(AccessControlScopeDestroyStatus.Completed, closed.Status);

        bool handled = false;
        AccessControlInboxStore inbox = new(
            dbContext,
            new FixedClock(Now.AddMinutes(2)),
            new SequenceIdGenerator());
        InboxProcessResult result = await inbox.ProcessAsync(
            new InboxMessageRecord(
                Id(983),
                "handler-a",
                "gma.access-control.test.v1",
                "access-control-test",
                1,
                Coordinate.TransportScopeId,
                Now.AddMinutes(2)),
            _ =>
            {
                handled = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(InboxProcessStatus.Suppressed, result.Status);
        Assert.False(handled);
        Assert.False(await dbContext.InboxMessages.AnyAsync(message =>
            message.Id == Id(983)));
    }

    [Fact]
    public async Task Missing_scope_preserves_zero_selection_after_unrelated_revisions()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext dbContext =
            await CreateDbContextAsync(connection);
        for (int index = 0; index < 4; index++)
        {
            await AccessControlManagementLock.AcquireAsync(
                dbContext,
                CancellationToken.None);
        }

        AccessControlScopeCoordinate emptyCoordinate = new(
            AccessScope.Parse("tenant:tenant-empty"),
            "tenant-empty");
        AccessControlScopeLifecycleService lifecycle = CreateLifecycle(dbContext);
        AccessControlScopeSnapshot missing = await lifecycle.GetSnapshotAsync(
            emptyCoordinate,
            CancellationToken.None);
        Assert.Equal(AccessControlScopeStatus.Missing, missing.Status);
        Assert.Equal(0, missing.Revision);

        AccessControlScopeDestroyRequest request = new(
            Id(990),
            emptyCoordinate,
            ExpectedRevision: 0,
            BatchSize: 10);
        AccessControlScopeDestroyResult result = await lifecycle
            .DestroyBatchAsync(request, CancellationToken.None);
        Assert.Equal(AccessControlScopeDestroyStatus.Completed, result.Status);
        Assert.Equal(5, result.Receipt!.ResultingRevision);

        AccessControlScopeSnapshot closed = await lifecycle.GetSnapshotAsync(
            emptyCoordinate,
            CancellationToken.None);
        Assert.Equal(AccessControlScopeStatus.Closed, closed.Status);
        Assert.Equal(5, closed.Revision);
        Assert.Equal(0, closed.SelectedRevision);

        AccessControlScopeDestroyResult replay = await lifecycle
            .DestroyBatchAsync(request, CancellationToken.None);
        Assert.Equal(AccessControlScopeDestroyStatus.Replayed, replay.Status);
    }

    private static async Task<T[]> ExportAllAsync<T>(
        AccessControlScopeLifecycleService lifecycle,
        long revision,
        AccessControlScopeExportStore store)
        where T : AccessControlScopeExportRecord
    {
        List<T> records = [];
        string? cursor = null;
        do
        {
            AccessControlScopeExportPage page = await lifecycle.ExportAsync(
                new AccessControlScopeExportRequest(
                    Coordinate,
                    revision,
                    store,
                    cursor,
                    PageSize: 1),
                CancellationToken.None);
            Assert.Equal(AccessControlScopeExportStatus.Completed, page.Status);
            records.AddRange(page.Records.Cast<T>());
            cursor = page.HasMore ? page.NextCursor : null;
        }
        while (cursor is not null);

        return records.ToArray();
    }

    private static async Task<SeededScope> SeedAsync(
        AccessControlDbContext dbContext)
    {
        Guid roleId = Id(1);
        AccessRole role = new(roleId, "operator", Now);
        dbContext.Roles.Add(role);
        dbContext.RolePermissions.Add(new AccessRolePermission(
            Id(2),
            roleId,
            "properties.read",
            Now));
        AccessSubject selectedOnly = AccessSubject.User("selected-only");
        AccessSubject shared = AccessSubject.User("shared");
        dbContext.Principals.AddRange(
            new AccessPrincipal(selectedOnly, Now),
            new AccessPrincipal(shared, Now));
        dbContext.SubjectRoleAssignments.AddRange(
            new AccessSubjectRoleAssignment(
                Id(3), selectedOnly, roleId, SelectedRoomScope, Now),
            new AccessSubjectRoleAssignment(
                Id(4), shared, roleId, SelectedScope, Now),
            new AccessSubjectRoleAssignment(
                Id(5), shared, roleId, SiblingScope, Now));

        AccessProfileSubject actor = new(
            AccessProfileSubjectKind.AdminActor,
            "operator");
        AccessProfile owned = AccessProfile.Create(
            Id(10),
            SelectedScope.Value,
            "property-manager",
            "Property manager",
            null,
            ["properties.read"],
            actor,
            Id(11),
            Now).Value;
        AccessProfile ancestor = AccessProfile.Create(
            Id(20),
            TenantScope.Value,
            "tenant-manager",
            "Tenant manager",
            null,
            ["properties.read"],
            actor,
            Id(21),
            Now).Value;
        dbContext.AccessProfiles.AddRange(owned, ancestor);
        dbContext.AccessProfileAssignments.AddRange(
            CreateAssignment(Id(12), owned.Id, SelectedScope, selectedOnly),
            CreateAssignment(Id(22), ancestor.Id, SelectedScope, selectedOnly),
            CreateAssignment(Id(23), ancestor.Id, SiblingScope, shared));
        owned.RecordAssignmentChange(
            Id(13),
            DomainChangeKind.Assigned,
            actor,
            ToDomain(selectedOnly),
            Now.AddMinutes(1),
            SelectedScope.Value);
        ancestor.RecordAssignmentChange(
            Id(24),
            DomainChangeKind.Assigned,
            actor,
            ToDomain(selectedOnly),
            Now.AddMinutes(1),
            SelectedScope.Value);
        ancestor.RecordAssignmentChange(
            Id(25),
            DomainChangeKind.Assigned,
            actor,
            ToDomain(shared),
            Now.AddMinutes(2),
            SiblingScope.Value);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return new SeededScope(roleId, owned.Id, ancestor.Id);
    }

    private static AccessProfileAssignment CreateAssignment(
        Guid id,
        Guid profileId,
        AccessScope assignmentScope,
        AccessSubject subject) =>
        AccessProfileAssignment.Create(
            id,
            profileId,
            assignmentScope.Value,
            ToDomain(subject),
            new AccessProfileSubject(
                AccessProfileSubjectKind.AdminActor,
                "operator"),
            Now).Value;

    private static AccessProfileSubject ToDomain(AccessSubject subject) =>
        new((AccessProfileSubjectKind)(int)subject.Kind, subject.Id);

    private static AccessControlScopeLifecycleService CreateLifecycle(
        AccessControlDbContext dbContext) =>
        new(dbContext, new FixedClock(Now.AddHours(1)));

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<AccessControlDbContext> CreateDbContextAsync(
        SqliteConnection connection)
    {
        DbContextOptions<AccessControlDbContext> options =
            new DbContextOptionsBuilder<AccessControlDbContext>()
                .UseSqlite(connection)
                .Options;
        AccessControlDbContext dbContext = new(options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }

    private static Guid Id(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");

    private sealed record SeededScope(
        Guid RoleId,
        Guid OwnedProfileId,
        Guid AncestorProfileId);

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        private int next = 2_000;

        public Guid NewId() => Id(Interlocked.Increment(ref this.next));
    }
}
