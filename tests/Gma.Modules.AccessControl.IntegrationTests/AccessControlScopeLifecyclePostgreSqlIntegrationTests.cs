namespace Gma.Modules.AccessControl.IntegrationTests;

using Gma.Framework.AccessControl;
using Gma.Framework.Messaging;
using Gma.Framework.Messaging.Infrastructure;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.ValueObjects;
using Gma.Modules.AccessControl.IntegrationTests.Support;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using DomainChangeKind =
    Gma.Modules.AccessControl.Domain.Enums.AccessProfileChangeKind;
using LifecycleReceipt =
    Gma.Modules.AccessControl.Contracts.AccessControlScopeDestroyReceipt;

public sealed partial class AccessControlPostgreSqlIntegrationTests
{
    private static readonly AccessScope LifecycleRootScope =
        AccessScope.Parse("tenant:tenant-a/property:lifecycle-a");
    private static readonly AccessScope LifecycleRoomScope =
        AccessScope.Parse(
            "tenant:tenant-a/property:lifecycle-a/room:lifecycle-room");
    private static readonly AccessScope LifecycleSiblingScope =
        AccessScope.Parse("tenant:tenant-a/property:lifecycle-b");
    private static readonly AccessControlScopeCoordinate LifecycleCoordinate =
        new(LifecycleRootScope, "tenant-a-property-lifecycle-a");

    [DockerFact]
    public async Task Scope_lifecycle_is_bounded_and_safe_on_postgresql()
    {
        await using PostgreSqlContainer postgreSql =
            CreatePostgreSql("access_scope_lifecycle_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
        await MigrateAsync(connectionString);

        await using (AccessControlDbContext migration =
            CreateDbContext(connectionString))
        {
            Assert.Contains(
                "20260804051353_AddAccessControlScopeLifecycle",
                await migration.Database.GetAppliedMigrationsAsync());
            Assert.Contains(
                "20260805103330_HardenAccessControlScopeLifecycle",
                await migration.Database.GetAppliedMigrationsAsync());
        }

        LifecycleSeed seeded;
        await using (AccessControlDbContext writer =
            CreateDbContext(connectionString))
        {
            seeded = await SeedLifecycleAsync(writer);
        }

        AccessControlScopeDestroyRequest request;
        LifecycleReceipt receipt;
        await using (AccessControlDbContext lifecycleContext =
            CreateDbContext(connectionString))
        {
            AccessControlScopeLifecycleService lifecycle =
                new(lifecycleContext, new FixedClock(Now.AddHours(1)));
            AccessControlScopeSnapshot snapshot =
                await lifecycle.GetSnapshotAsync(
                    LifecycleCoordinate,
                    CancellationToken.None);

            Assert.Equal(AccessControlScopeStatus.Open, snapshot.Status);
            Assert.Equal(2, (await ExportLifecycleAllAsync<
                AccessControlRoleAssignmentExportRecord>(
                lifecycle,
                snapshot.Revision,
                AccessControlScopeExportStore.RoleAssignments)).Length);
            AccessControlProfileAssignmentExportRecord[] assignments =
                await ExportLifecycleAllAsync<
                    AccessControlProfileAssignmentExportRecord>(
                    lifecycle,
                    snapshot.Revision,
                    AccessControlScopeExportStore.ProfileAssignments);
            Assert.Equal(2, assignments.Length);
            Assert.Contains(assignments, assignment =>
                assignment.ProfileId == seeded.AncestorProfileId &&
                assignment.AssignmentScopeValue == LifecycleRootScope.Value);

            request = new AccessControlScopeDestroyRequest(
                Guid.NewGuid(),
                LifecycleCoordinate,
                snapshot.Revision,
                BatchSize: 1);
            AccessControlScopeDestroyResult result;
            int calls = 0;
            do
            {
                result = await lifecycle.DestroyBatchAsync(
                    request,
                    CancellationToken.None);
                calls++;
                Assert.True(calls < 40, "Destruction did not converge.");
            }
            while (result.Status ==
                AccessControlScopeDestroyStatus.InProgress);

            Assert.Equal(
                AccessControlScopeDestroyStatus.Completed,
                result.Status);
            receipt = Assert.IsType<LifecycleReceipt>(
                result.Receipt);
            Assert.True(receipt.CompletedBatchCount > 1);
            Assert.True(receipt.RemovedRecordCount > 0);
        }

        await using AccessControlDbContext verification =
            CreateDbContext(connectionString);
        Assert.False(await verification.InboxMessages.AnyAsync(message =>
            message.ScopeId == LifecycleCoordinate.TransportScopeId));
        Assert.False(await verification.SubjectRoleAssignments.AnyAsync(
            assignment =>
                assignment.ScopeValue == LifecycleRootScope.Value ||
                assignment.ScopeValue.StartsWith(
                    LifecycleRootScope.Value + "/")));
        Assert.Null(await verification.AccessProfiles.FindAsync(
            seeded.OwnedProfileId));
        Assert.NotNull(await verification.AccessProfiles.FindAsync(
            seeded.AncestorProfileId));
        Assert.DoesNotContain(
            await verification.AccessProfileAssignments.ToArrayAsync(),
            assignment => IsLifecycleOwned(assignment.AssignmentScopeValue));
        Assert.DoesNotContain(
            await verification.AccessProfileChanges.ToArrayAsync(),
            change => change.AssignmentScopeValue is not null &&
                IsLifecycleOwned(change.AssignmentScopeValue));
        Assert.False(await verification.Principals.AnyAsync(principal =>
            principal.Kind == (int)AccessSubjectKind.User &&
            principal.SubjectId == "lifecycle-selected-only"));
        Assert.True(await verification.Principals.AnyAsync(principal =>
            principal.Kind == (int)AccessSubjectKind.User &&
            principal.SubjectId == "lifecycle-shared"));
        Assert.True(await verification.Roles.AnyAsync(role =>
            role.Id == seeded.RoleId));
        Assert.True(await verification.RolePermissions.AnyAsync(permission =>
            permission.RoleId == seeded.RoleId));

        verification.SubjectRoleAssignments.Add(
            new AccessSubjectRoleAssignment(
                Guid.NewGuid(),
                AccessSubject.User("lifecycle-shared"),
                seeded.RoleId,
                LifecycleRoomScope,
                Now.AddHours(2)));
        await Assert.ThrowsAsync<AccessControlScopeClosedException>(() =>
            verification.SaveChangesAsync());
        verification.ChangeTracker.Clear();

        AccessControlScopeLifecycleService replayLifecycle =
            new(verification, new FixedClock(Now.AddHours(2)));
        AccessControlScopeDestroyResult replay =
            await replayLifecycle.DestroyBatchAsync(
                request,
                CancellationToken.None);
        AccessControlScopeDestroyResult conflict =
            await replayLifecycle.DestroyBatchAsync(
                request with { OperationId = Guid.NewGuid() },
                CancellationToken.None);
        Assert.Equal(
            AccessControlScopeDestroyStatus.Replayed,
            replay.Status);
        Assert.Equal(receipt, replay.Receipt);
        Assert.Equal(
            AccessControlScopeDestroyStatus.Conflict,
            conflict.Status);

        bool handled = false;
        AccessControlInboxStore inbox = new(
            verification,
            new FixedClock(Now.AddHours(3)),
            new RandomIdGenerator());
        InboxProcessResult lateMessage = await inbox.ProcessAsync(
            new InboxMessageRecord(
                Guid.NewGuid(),
                "lifecycle-handler",
                "gma.access-control.lifecycle.v1",
                "access-control-lifecycle",
                1,
                LifecycleCoordinate.TransportScopeId,
                Now.AddHours(3)),
            _ =>
            {
                handled = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        Assert.Equal(InboxProcessStatus.Suppressed, lateMessage.Status);
        Assert.False(handled);
        await VerifyTerminalProtectionAsync(
            connectionString,
            LifecycleCoordinate);
    }

    private static async Task VerifyTerminalProtectionAsync(
        string connectionString,
        AccessControlScopeCoordinate coordinate)
    {
        await using (AccessControlDbContext stateMutation =
                     CreateDbContext(connectionString))
        {
            PostgresException failure = await Assert.ThrowsAsync<PostgresException>(
                () => stateMutation.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE access.access_scope_states
                    SET "IsClosed" = FALSE
                    WHERE "ScopeValue" = {coordinate.RootScope.Value};
                    """));
            Assert.Equal("P0001", failure.SqlState);
            Assert.Contains("closed access-control scope", failure.MessageText);
        }

        await using (AccessControlDbContext receiptMutation =
                     CreateDbContext(connectionString))
        {
            PostgresException failure = await Assert.ThrowsAsync<PostgresException>(
                () => receiptMutation.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE access.access_scope_destroy_receipts
                    SET "RemovedRecordCount" = "RemovedRecordCount" + 1
                    WHERE "ScopeValue" = {coordinate.RootScope.Value};
                    """));
            Assert.Equal("P0001", failure.SqlState);
            Assert.Contains("append-only", failure.MessageText);
        }
    }

    private static async Task<LifecycleSeed> SeedLifecycleAsync(
        AccessControlDbContext dbContext)
    {
        Guid roleId = Guid.NewGuid();
        AccessRole role = new(roleId, "lifecycle-operator", Now);
        dbContext.Roles.Add(role);
        dbContext.RolePermissions.Add(new AccessRolePermission(
            Guid.NewGuid(),
            roleId,
            "properties.read",
            Now));
        AccessSubject selectedOnly =
            AccessSubject.User("lifecycle-selected-only");
        AccessSubject shared = AccessSubject.User("lifecycle-shared");
        dbContext.Principals.AddRange(
            new AccessPrincipal(selectedOnly, Now),
            new AccessPrincipal(shared, Now));
        dbContext.SubjectRoleAssignments.AddRange(
            new AccessSubjectRoleAssignment(
                Guid.NewGuid(),
                selectedOnly,
                roleId,
                LifecycleRoomScope,
                Now),
            new AccessSubjectRoleAssignment(
                Guid.NewGuid(),
                shared,
                roleId,
                LifecycleRootScope,
                Now),
            new AccessSubjectRoleAssignment(
                Guid.NewGuid(),
                shared,
                roleId,
                LifecycleSiblingScope,
                Now));

        AccessProfileSubject actor = new(
            AccessProfileSubjectKind.AdminActor,
            "lifecycle-operator");
        AccessProfile owned = AccessProfile.Create(
            Guid.NewGuid(),
            LifecycleRootScope.Value,
            "lifecycle-property-manager",
            "Lifecycle property manager",
            null,
            ["properties.read"],
            actor,
            Guid.NewGuid(),
            Now).Value;
        AccessProfile ancestor = AccessProfile.Create(
            Guid.NewGuid(),
            TenantScope.Value,
            "lifecycle-tenant-manager",
            "Lifecycle tenant manager",
            null,
            ["properties.read"],
            actor,
            Guid.NewGuid(),
            Now).Value;
        dbContext.AccessProfiles.AddRange(owned, ancestor);
        dbContext.AccessProfileAssignments.AddRange(
            CreateAssignment(
                owned.Id,
                selectedOnly,
                LifecycleRootScope),
            CreateAssignment(
                ancestor.Id,
                selectedOnly,
                LifecycleRootScope),
            CreateAssignment(
                ancestor.Id,
                shared,
                LifecycleSiblingScope));
        owned.RecordAssignmentChange(
            Guid.NewGuid(),
            DomainChangeKind.Assigned,
            actor,
            ToDomain(selectedOnly),
            Now.AddMinutes(1),
            LifecycleRootScope.Value);
        ancestor.RecordAssignmentChange(
            Guid.NewGuid(),
            DomainChangeKind.Assigned,
            actor,
            ToDomain(selectedOnly),
            Now.AddMinutes(1),
            LifecycleRootScope.Value);
        ancestor.RecordAssignmentChange(
            Guid.NewGuid(),
            DomainChangeKind.Assigned,
            actor,
            ToDomain(shared),
            Now.AddMinutes(2),
            LifecycleSiblingScope.Value);

        InboxMessage processed = InboxMessage.Create(
            Guid.NewGuid(),
            "lifecycle-handler",
            "gma.access-control.lifecycle.v1",
            "access-control-lifecycle",
            1,
            LifecycleCoordinate.TransportScopeId,
            Now,
            Now);
        processed.MarkProcessing("lifecycle-worker", Now);
        processed.MarkProcessed(Now.AddMinutes(1));
        dbContext.InboxMessages.Add(processed);

        await dbContext.SaveChangesAsync();
        return new LifecycleSeed(roleId, owned.Id, ancestor.Id);
    }

    private static async Task<T[]> ExportLifecycleAllAsync<T>(
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
                    LifecycleCoordinate,
                    revision,
                    store,
                    cursor,
                    PageSize: 1),
                CancellationToken.None);
            Assert.Equal(
                AccessControlScopeExportStatus.Completed,
                page.Status);
            records.AddRange(page.Records.Cast<T>());
            cursor = page.HasMore ? page.NextCursor : null;
        }
        while (cursor is not null);

        return records.ToArray();
    }

    private static bool IsLifecycleOwned(string scopeValue) =>
        scopeValue == LifecycleRootScope.Value ||
        scopeValue.StartsWith(
            LifecycleRootScope.Value + "/",
            StringComparison.Ordinal);

    private sealed record LifecycleSeed(
        Guid RoleId,
        Guid OwnedProfileId,
        Guid AncestorProfileId);
}
