namespace Gma.Modules.AccessControl.Tests.Persistence;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.ValueObjects;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Entities;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ContractProfileStatus = Gma.Modules.AccessControl.Contracts.AccessProfileStatus;

[Trait("Category", "Unit")]
public sealed class AccessControlSubjectAuthoritySnapshotReaderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly AccessScope TenantScope =
        AccessScope.Parse("tenant:tenant-a");
    private static readonly AccessScope PropertyScope =
        AccessScope.Parse("tenant:tenant-a/property:property-a");
    private static readonly AccessScope RoomScope = AccessScope.Parse(
        "tenant:tenant-a/property:property-a/room:room-a");
    private static readonly AccessScope OtherPropertyScope =
        AccessScope.Parse("tenant:tenant-a/property:property-b");

    [Fact]
    public async Task Empty_snapshot_is_zero_write_and_bound_to_subject_and_root()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext context =
            await CreateContextAsync(connection);
        context.ChangeTracker.Clear();
        AccessControlSubjectAuthoritySnapshotReader reader = new(
            context,
            new FixedTimeProvider(Now));

        AccessControlSubjectAuthoritySnapshot first = await reader.ReadAsync(
            new(AccessSubjectKind.User, "first-user", TenantScope, [TenantScope]));
        AccessControlSubjectAuthoritySnapshot replay = await reader.ReadAsync(
            new(AccessSubjectKind.User, "first-user", TenantScope, [TenantScope]));
        AccessControlSubjectAuthoritySnapshot otherSubject =
            await reader.ReadAsync(
                new(AccessSubjectKind.User, "second-user", TenantScope, [TenantScope]));
        AccessControlSubjectAuthoritySnapshot otherRoot = await reader.ReadAsync(
            new(AccessSubjectKind.User, "first-user", PropertyScope, [PropertyScope]));
        AccessControlSubjectAuthoritySnapshot otherTarget = await reader.ReadAsync(
            new(AccessSubjectKind.User, "first-user", TenantScope, [PropertyScope]));

        Assert.Equal(AccessControlAuthoritySnapshotStatus.Stable, first.Status);
        Assert.Equal(2, first.ContractVersion);
        Assert.Equal(0, first.ManagementRevision);
        Assert.Equal(AccessSubjectKind.User, first.SubjectKind);
        Assert.Equal("first-user", first.SubjectId);
        Assert.Equal(TenantScope, first.RootScope);
        Assert.Equal([TenantScope], first.ExpectedTargetScopes);
        Assert.Empty(first.RoleGrants);
        Assert.Empty(first.ProfileGrants);
        Assert.Equal(first.FactsSha256, replay.FactsSha256);
        Assert.NotEqual(first.FactsSha256, otherSubject.FactsSha256);
        Assert.NotEqual(first.FactsSha256, otherRoot.FactsSha256);
        Assert.NotEqual(first.FactsSha256, otherTarget.FactsSha256);
        Assert.Equal(
            0,
            await AccessControlManagementLock.ReadRevisionAsync(
                context,
                CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Snapshot_orders_all_related_grants_and_includes_ancestor_profile()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext context =
            await CreateContextAsync(connection);
        AccessSubject subject = AccessSubject.User("operator-a");
        await SeedAuthorityAsync(context, subject);
        context.ChangeTracker.Clear();
        AccessControlSubjectAuthoritySnapshotReader reader = new(
            context,
            new FixedTimeProvider(Now.AddMinutes(30)));

        AccessControlSubjectAuthoritySnapshot snapshot = await reader.ReadAsync(
            new(subject.Kind, subject.Id, PropertyScope, [PropertyScope]));
        AccessControlSubjectAuthoritySnapshot replay = await reader.ReadAsync(
            new(subject.Kind, subject.Id, PropertyScope, [PropertyScope]));

        Assert.Equal(
            AccessControlAuthoritySnapshotStatus.Stable,
            snapshot.Status);
        Assert.Equal(snapshot.FactsSha256, replay.FactsSha256);
        Assert.Equal(
            [
                AccessScope.Global.Value,
                TenantScope.Value,
                PropertyScope.Value,
                RoomScope.Value
            ],
            snapshot.RoleGrants.Select(grant => grant.Scope.Value));
        AccessControlAuthorityProfileGrant profile = Assert.Single(
            snapshot.ProfileGrants);
        Assert.Equal(PropertyScope, profile.AssignmentScope);
        Assert.Equal(TenantScope, profile.OwnerScope);
        Assert.Equal(["properties.read", "staff.read"], profile.Permissions);
        Assert.Equal(
            0,
            await AccessControlManagementLock.ReadRevisionAsync(
                context,
                CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("tenant:tenant-a", AccessControlAuthoritySnapshotStatus.Closed)]
    [InlineData(
        "tenant:tenant-a/property:property-a",
        AccessControlAuthoritySnapshotStatus.Closed)]
    [InlineData(
        "tenant:tenant-a/property:property-a/room:room-a",
        AccessControlAuthoritySnapshotStatus.Closed)]
    [InlineData(
        "tenant:tenant-a/property:property-b",
        AccessControlAuthoritySnapshotStatus.Stable)]
    [InlineData(
        "tenant:tenant-a/property:property-a/room:room-a/bed:bed-a",
        AccessControlAuthoritySnapshotStatus.Stable)]
    [InlineData(
        "malformed unrelated lifecycle row",
        AccessControlAuthoritySnapshotStatus.Stable)]
    public async Task Lifecycle_closure_is_limited_to_exact_target_ancestors(
        string closedScopeValue,
        AccessControlAuthoritySnapshotStatus expectedStatus)
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext context =
            await CreateContextAsync(connection);
        AccessControlScopeState state = AccessControlScopeState.Create(
            closedScopeValue,
            "tenant-a");
        Assert.Equal(
            AccessControlScopeCloseTransition.Completed,
            state.Close(Id(300), new string('a', 64), 1, Now));
        context.AccessScopeStates.Add(state);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        AccessControlSubjectAuthoritySnapshotReader reader = new(
            context,
            new FixedTimeProvider(Now));

        AccessControlSubjectAuthoritySnapshot snapshot = await reader.ReadAsync(
            new(
                AccessSubjectKind.User,
                "operator-a",
                PropertyScope,
                [RoomScope]));

        Assert.Equal(expectedStatus, snapshot.Status);
        Assert.Equal([RoomScope], snapshot.ExpectedTargetScopes);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Invalid_or_unbounded_target_sets_fail_closed()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext context =
            await CreateContextAsync(connection);
        AccessControlSubjectAuthoritySnapshotReader reader = new(
            context,
            new FixedTimeProvider(Now));
        AccessScope[] tooMany = Enumerable.Range(
                0,
                IAccessControlSubjectAuthoritySnapshotReader
                    .MaximumExpectedTargetScopeCount + 1)
            .Select(index => AccessScope.Parse(
                $"tenant:tenant-a/property:property-{index}"))
            .ToArray();
        AccessControlSubjectAuthoritySnapshotRequest[] invalid =
        [
            new(AccessSubjectKind.User, "operator-a", TenantScope, []),
            new(
                AccessSubjectKind.User,
                "operator-a",
                TenantScope,
                [PropertyScope, PropertyScope]),
            new(
                AccessSubjectKind.User,
                "operator-a",
                PropertyScope,
                [OtherPropertyScope]),
            new(AccessSubjectKind.User, "operator-a", TenantScope, tooMany),
            new(
                AccessSubjectKind.User,
                "operator-a",
                TenantScope,
                null!)
        ];

        foreach (AccessControlSubjectAuthoritySnapshotRequest request in invalid)
        {
            AccessControlSubjectAuthoritySnapshot snapshot =
                await reader.ReadAsync(request);
            Assert.Equal(
                AccessControlAuthoritySnapshotStatus.Invalid,
                snapshot.Status);
        }
    }

    [Fact]
    public async Task Global_root_checks_exact_target_ancestors_not_unrelated_tenants()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext context =
            await CreateContextAsync(connection);
        AccessControlScopeState unrelated = AccessControlScopeState.Create(
            "tenant:tenant-b",
            "tenant-b");
        Assert.Equal(
            AccessControlScopeCloseTransition.Completed,
            unrelated.Close(Id(400), new string('a', 64), 1, Now));
        context.AccessScopeStates.Add(unrelated);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        AccessControlSubjectAuthoritySnapshotReader reader = new(
            context,
            new FixedTimeProvider(Now));

        AccessControlSubjectAuthoritySnapshot stable = await reader.ReadAsync(
            new(
                AccessSubjectKind.User,
                "operator-a",
                AccessScope.Global,
                [PropertyScope]));

        Assert.Equal(AccessControlAuthoritySnapshotStatus.Stable, stable.Status);
        AccessControlScopeState targetAncestor =
            AccessControlScopeState.Create(TenantScope.Value, "tenant-a");
        Assert.Equal(
            AccessControlScopeCloseTransition.Completed,
            targetAncestor.Close(Id(401), new string('b', 64), 1, Now));
        context.AccessScopeStates.Add(targetAncestor);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        AccessControlSubjectAuthoritySnapshot closed = await reader.ReadAsync(
            new(
                AccessSubjectKind.User,
                "operator-a",
                AccessScope.Global,
                [PropertyScope]));

        Assert.Equal(AccessControlAuthoritySnapshotStatus.Closed, closed.Status);
        Assert.Equal([PropertyScope], closed.ExpectedTargetScopes);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Snapshot_excludes_revoked_and_elapsed_leases_at_the_observed_clock()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext context = await CreateContextAsync(connection);
        AccessSubject subject = AccessSubject.User("lease-operator");
        context.Principals.Add(new AccessPrincipal(subject, Now.AddDays(-1)));
        DateTimeOffset?[] expiries =
        [Now.AddHours(1), Now, Now.AddMinutes(-1), Now.AddHours(2), null, Now.AddMinutes(5)];
        for (int index = 0; index < expiries.Length; index++)
        {
            Guid roleId = Id(500 + index);
            context.Roles.Add(new AccessRole(roleId, $"lease-role-{index}", Now.AddDays(-1)));
            AccessSubjectRoleAssignment assignment = new(
                Id(600 + index), subject, roleId, PropertyScope, Now.AddDays(-1), expiries[index]);
            if (index == 3)
            {
                assignment.Revoke(Now.AddMinutes(-1));
            }

            context.SubjectRoleAssignments.Add(assignment);
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        AccessControlSubjectAuthoritySnapshotReader reader = new(context, new FixedTimeProvider(Now));

        AccessControlSubjectAuthoritySnapshot snapshot = await reader.ReadAsync(
            new(subject.Kind, subject.Id, PropertyScope, [PropertyScope]));

        Assert.Equal(AccessControlAuthoritySnapshotStatus.Stable, snapshot.Status);
        Assert.Equal(Now, snapshot.ObservedAtUtc);
        Assert.Equal([Id(600), Id(604), Id(605)], snapshot.RoleGrants.Select(grant => grant.AssignmentId));
        Assert.Equal(Now.AddMinutes(5), snapshot.EarliestExpiryAtUtc);
        Assert.Empty(context.ChangeTracker.Entries());

        AccessControlSubjectAuthoritySnapshotReader laterReader = new(
            context, new FixedTimeProvider(Now.AddMinutes(5)));
        AccessControlSubjectAuthoritySnapshot later = await laterReader.ReadAsync(
            new(subject.Kind, subject.Id, PropertyScope, [PropertyScope]));

        Assert.Equal(AccessControlAuthoritySnapshotStatus.Stable, later.Status);
        Assert.Equal([Id(600), Id(604)], later.RoleGrants.Select(grant => grant.AssignmentId));
        Assert.Equal(Now.AddHours(1), later.EarliestExpiryAtUtc);
        Assert.NotEqual(snapshot.FactsSha256, later.FactsSha256);
        Assert.Equal(snapshot.ManagementRevision, later.ManagementRevision);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Cancelled_read_propagates_cancellation_instead_of_returning_unavailable()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext context = await CreateContextAsync(connection);
        AccessControlSubjectAuthoritySnapshotReader reader = new(context, new FixedTimeProvider(Now));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(
            new(AccessSubjectKind.User, "operator-a", PropertyScope, [PropertyScope]),
            cancellation.Token));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void Persistence_registration_resolves_the_public_reader_once_per_scope()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>("Persistence:Provider", "SqlServer"),
            new KeyValuePair<string, string?>("ConnectionStrings:SqlServer", "Server=localhost;Database=gma;TrustServerCertificate=True")
        ]);
        builder.AddAccessControlPersistence();
        builder.AddAccessControlPersistence();
        ServiceDescriptor registration = Assert.Single(builder.Services,
            item => item.ServiceType == typeof(IAccessControlSubjectAuthoritySnapshotReader));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        using ServiceProvider provider = builder.Services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();

        IAccessControlSubjectAuthoritySnapshotReader reader = first.ServiceProvider
            .GetRequiredService<IAccessControlSubjectAuthoritySnapshotReader>();

        Assert.IsType<AccessControlSubjectAuthoritySnapshotReader>(reader);
        Assert.Same(reader, first.ServiceProvider.GetRequiredService<IAccessControlSubjectAuthoritySnapshotReader>());
        Assert.NotSame(reader, second.ServiceProvider.GetRequiredService<IAccessControlSubjectAuthoritySnapshotReader>());
    }

    [Theory]
    [InlineData(1, ContractProfileStatus.Active)]
    [InlineData(2, ContractProfileStatus.Archived)]
    public async Task Snapshot_maps_supported_stored_profile_states(int storedStatus, ContractProfileStatus expected)
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext context = await CreateContextAsync(connection);
        AccessSubject subject = AccessSubject.User("profile-state-reader");
        await SeedAuthorityAsync(context, subject);
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE access_profiles SET Status = {storedStatus}");
        context.ChangeTracker.Clear();
        AccessControlSubjectAuthoritySnapshotReader reader = new(context, new FixedTimeProvider(Now));

        AccessControlSubjectAuthoritySnapshot snapshot = await reader.ReadAsync(
            new(subject.Kind, subject.Id, PropertyScope, [PropertyScope]));

        Assert.Equal(AccessControlAuthoritySnapshotStatus.Stable, snapshot.Status);
        Assert.Equal(expected, Assert.Single(snapshot.ProfileGrants).ProfileStatus);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public async Task Snapshot_rejects_unknown_or_corrupt_stored_profile_states(int storedStatus)
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext context = await CreateContextAsync(connection);
        AccessSubject subject = AccessSubject.User("corrupt-profile-reader");
        await SeedAuthorityAsync(context, subject);
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE access_profiles SET Status = {storedStatus}");
        context.ChangeTracker.Clear();
        AccessControlSubjectAuthoritySnapshotReader reader = new(context, new FixedTimeProvider(Now));

        AccessControlSubjectAuthoritySnapshot snapshot = await reader.ReadAsync(
            new(subject.Kind, subject.Id, PropertyScope, [PropertyScope]));

        Assert.Equal(AccessControlAuthoritySnapshotStatus.Invalid, snapshot.Status);
        Assert.Empty(snapshot.ProfileGrants);
        Assert.Empty(snapshot.RoleGrants);
        Assert.Null(snapshot.FactsSha256);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static async Task SeedAuthorityAsync(
        AccessControlDbContext context,
        AccessSubject subject)
    {
        context.Principals.Add(new AccessPrincipal(subject, Now));
        (string Name, AccessScope Scope, int Id)[] roles =
        [
            ("z-global", AccessScope.Global, 1),
            ("a-ancestor", TenantScope, 2),
            ("m-root", PropertyScope, 3),
            ("n-descendant", RoomScope, 4)
        ];
        foreach ((string name, AccessScope scope, int id) in roles.Reverse())
        {
            Guid roleId = Id(id);
            context.Roles.Add(new AccessRole(roleId, name, Now));
            context.RolePermissions.Add(new AccessRolePermission(
                Id(id + 20),
                roleId,
                $"permission.{id}",
                Now));
            context.SubjectRoleAssignments.Add(new AccessSubjectRoleAssignment(
                Id(id + 40),
                subject,
                roleId,
                scope,
                Now));
        }

        AccessProfileSubject actor = new(
            AccessProfileSubjectKind.AdminActor,
            "operator");
        AccessProfile profile = AccessProfile.Create(
            Id(100),
            TenantScope.Value,
            "ancestor-manager",
            "Ancestor manager",
            description: null,
            ["staff.read", "properties.read"],
            actor,
            Id(101),
            Now).Value;
        context.AccessProfiles.Add(profile);
        context.AccessProfileAssignments.Add(AccessProfileAssignment.Create(
            Id(102),
            profile.Id,
            PropertyScope.Value,
            new AccessProfileSubject(
                AccessProfileSubjectKind.User,
                subject.Id),
            actor,
            Now).Value);
        await context.SaveChangesAsync();
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<AccessControlDbContext> CreateContextAsync(
        SqliteConnection connection)
    {
        AccessControlDbContext context = new(
            new DbContextOptionsBuilder<AccessControlDbContext>()
                .UseSqlite(connection)
                .Options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static Guid Id(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
