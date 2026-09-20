namespace Gma.Modules.AccessControl.IntegrationTests;

using System.Data.Common;
using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.IntegrationTests.Support;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Entities;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;
using Xunit;

public sealed partial class AccessControlPostgreSqlIntegrationTests
{
    [DockerFact]
    public async Task Authority_snapshot_target_lifecycle_is_bounded_on_postgresql()
    {
        await using PostgreSqlContainer postgreSql = CreatePostgreSql(
            "access_authority_snapshot_target_lifecycle_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
        await MigrateAsync(connectionString);
        AccessScope root = AccessScope.Parse(
            "tenant:snapshot-tenant/property:property-a");
        AccessScope target = AccessScope.Parse(
            "tenant:snapshot-tenant/property:property-a/room:room-a");
        AccessControlScopeState unrelated = ClosedState(
            "tenant:snapshot-tenant/property:property-b",
            "snapshot-unrelated",
            901);
        AccessControlScopeState malformed = ClosedState(
            "malformed unrelated lifecycle row",
            "snapshot-malformed",
            902);

        await using AccessControlDbContext context = CreateDbContext(
            connectionString);
        context.AccessScopeStates.AddRange(unrelated, malformed);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        AccessControlSubjectAuthoritySnapshotReader reader = new(
            context,
            new SnapshotFixedTimeProvider(Now));
        AccessControlSubjectAuthoritySnapshot stable = await reader.ReadAsync(
            new(
                AccessSubjectKind.User,
                "snapshot-operator",
                root,
                [target]));

        Assert.Equal(AccessControlAuthoritySnapshotStatus.Stable, stable.Status);
        Assert.Equal(2, stable.ContractVersion);
        Assert.Equal([target], stable.ExpectedTargetScopes);
        Assert.Empty(context.ChangeTracker.Entries());

        context.AccessScopeStates.Add(ClosedState(
            "tenant:snapshot-tenant",
            "snapshot-ancestor",
            903));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        AccessControlSubjectAuthoritySnapshot closed = await reader.ReadAsync(
            new(
                AccessSubjectKind.User,
                "snapshot-operator",
                root,
                [target]));

        Assert.Equal(AccessControlAuthoritySnapshotStatus.Closed, closed.Status);
        Assert.Equal(stable.ManagementRevision, closed.ManagementRevision);
        Assert.Equal(stable.FactsSha256, closed.FactsSha256);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [DockerFact]
    public async Task Authority_snapshot_discards_grants_when_management_revision_changes_between_reads()
    {
        await using PostgreSqlContainer postgreSql = CreatePostgreSql("access_authority_revision_drift_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
        await MigrateAsync(connectionString);
        AccessSubject subject = AccessSubject.User("snapshot-drift-operator");
        AccessControlSubjectAuthoritySnapshotRequest request = new(
            subject.Kind, subject.Id, TenantScope, [TenantScope]);
        await using AccessControlDbContext writer = CreateDbContext(connectionString);
        Guid roleId = Guid.NewGuid();
        writer.Principals.Add(new AccessPrincipal(subject, Now));
        writer.Roles.Add(new AccessRole(roleId, "snapshot-reader", Now));
        writer.SubjectRoleAssignments.Add(new AccessSubjectRoleAssignment(
            Guid.NewGuid(), subject, roleId, TenantScope, Now));
        AccessProfile profile = CreateProfile("snapshot-reader", ["records.read"]);
        writer.AccessProfiles.Add(profile);
        writer.AccessProfileAssignments.Add(CreateAssignment(profile.Id, subject));
        await writer.SaveChangesAsync();
        writer.ChangeTracker.Clear();
        AccessControlSubjectAuthoritySnapshot before = await new AccessControlSubjectAuthoritySnapshotReader(
            writer, new SnapshotFixedTimeProvider(Now)).ReadAsync(request);
        Assert.Equal(AccessControlAuthoritySnapshotStatus.Stable, before.Status);
        Assert.Single(before.RoleGrants);
        Assert.Single(before.ProfileGrants);
        Assert.NotNull(before.FactsSha256);

        SnapshotRevisionDriftInterceptor drift = new(writer);
        await using AccessControlDbContext context = CreateDbContext(connectionString, drift);
        AccessControlSubjectAuthoritySnapshot snapshot = await new AccessControlSubjectAuthoritySnapshotReader(
            context, new SnapshotFixedTimeProvider(Now)).ReadAsync(request);

        Assert.Equal(2, drift.RevisionReads);
        Assert.Equal(AccessControlAuthoritySnapshotStatus.Stale, snapshot.Status);
        Assert.Equal(before.ManagementRevision, snapshot.ManagementRevision);
        Assert.Equal(subject.Id, snapshot.SubjectId);
        Assert.Equal(TenantScope, snapshot.RootScope);
        Assert.Equal([TenantScope], snapshot.ExpectedTargetScopes);
        Assert.Null(snapshot.FactsSha256);
        Assert.Null(snapshot.EarliestExpiryAtUtc);
        Assert.Empty(snapshot.RoleGrants);
        Assert.Empty(snapshot.ProfileGrants);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(writer.ChangeTracker.Entries());
        Assert.All(drift.Commands, command => Assert.StartsWith("SELECT", command.TrimStart()));
        Assert.Equal(before.ManagementRevision + 1,
            await AccessControlManagementLock.ReadRevisionAsync(writer, CancellationToken.None));
    }

    private sealed class SnapshotRevisionDriftInterceptor(AccessControlDbContext writer) : DbCommandInterceptor
    {
        public int RevisionReads { get; private set; }
        public List<string> Commands { get; } = [];

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            this.Commands.Add(command.CommandText);
            if (command.CommandText.Contains("bootstrap_state", StringComparison.Ordinal) &&
                ++this.RevisionReads == 2)
            {
                // A separate connection commits the real management revision after
                // the snapshot's first read and before its final read. No sleep/race.
                await AccessControlManagementLock.AcquireAsync(writer, cancellationToken);
            }

            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The snapshot reader must not issue writes.");
    }

    private static AccessControlScopeState ClosedState(
        string scopeValue,
        string transportScopeId,
        int operationSeed)
    {
        AccessControlScopeState state = AccessControlScopeState.Create(
            scopeValue,
            transportScopeId);
        Assert.Equal(
            AccessControlScopeCloseTransition.Completed,
            state.Close(
                new Guid(operationSeed, 0, 0, new byte[8]),
                new string('a', 64),
                1,
                Now));
        return state;
    }

    private sealed class SnapshotFixedTimeProvider(DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
