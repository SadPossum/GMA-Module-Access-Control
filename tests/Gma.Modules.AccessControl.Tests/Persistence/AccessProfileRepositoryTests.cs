namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.Pagination;
using Gma.Framework.Permissions;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessProfileRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly AccessSubject Actor = AccessSubject.AdminActor("actor-a");

    [Fact]
    public async Task Profile_catalog_is_scope_isolated_and_bounded()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext dbContext = await CreateDbContextAsync(connection);
        AccessProfileRepository repository = new(dbContext);
        AccessScope tenantA = AccessScope.Parse("tenant:tenant-a");
        AccessScope tenantB = AccessScope.Parse("tenant:tenant-b");
        AccessProfile profileA1 = CreateProfile(tenantA, "front-desk");
        AccessProfile profileA2 = CreateProfile(tenantA, "manager");
        AccessProfile profileB = CreateProfile(tenantB, "front-desk");
        repository.Add(profileA1);
        repository.Add(profileA2);
        repository.Add(profileB);
        await dbContext.SaveChangesAsync();

        AccessControlPage<AccessProfileDetails> firstPage = await repository.ListAsync(
            tenantA, includeArchived: false, PageRequest.Normalize(1, 1), CancellationToken.None);
        AccessControlPage<AccessProfileDetails> secondPage = await repository.ListAsync(
            tenantA, includeArchived: false, PageRequest.Normalize(2, 1), CancellationToken.None);
        AccessControlPage<AccessProfileDetails> tenantBPage = await repository.ListAsync(
            tenantB, includeArchived: false, PageRequest.Normalize(1, 10), CancellationToken.None);
        AccessProfileDetails? crossScope = await repository.GetDetailsAsync(
            profileA1.Id, tenantB, CancellationToken.None);

        Assert.Equal("front-desk", Assert.Single(firstPage.Items).Key);
        Assert.True(firstPage.HasMore);
        Assert.Equal("manager", Assert.Single(secondPage.Items).Key);
        Assert.False(secondPage.HasMore);
        Assert.Equal(profileB.Id, Assert.Single(tenantBPage.Items).Id);
        Assert.Null(crossScope);
    }

    [Fact]
    public async Task Profile_assignments_are_scope_isolated_and_bounded()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext dbContext = await CreateDbContextAsync(connection);
        SequenceIdGenerator ids = new();
        AccessControlRbacRepository rbac = CreateRbacRepository(dbContext, ids);
        AccessProfileRepository repository = new(dbContext);
        AccessScope tenantA = AccessScope.Parse("tenant:tenant-a");
        AccessScope tenantB = AccessScope.Parse("tenant:tenant-b");
        AccessProfile profile = CreateProfile(tenantA, "front-desk", ["reservations.read"]);
        AccessSubject subjectA = AccessSubject.User("user-a");
        AccessSubject subjectB = AccessSubject.User("user-b");
        await rbac.EnsureSubjectAsync(subjectA, Now, CancellationToken.None);
        await rbac.EnsureSubjectAsync(subjectB, Now, CancellationToken.None);
        repository.Add(profile);
        AccessProfileAssignment assignmentA = CreateAssignment(profile.Id, subjectA, ids.NewId());
        AccessProfileAssignment assignmentB = CreateAssignment(profile.Id, subjectB, ids.NewId());
        repository.AddAssignment(assignmentA);
        repository.AddAssignment(assignmentB);
        profile.RecordAssignmentChange(ids.NewId(), AccessProfileChangeKind.Assigned, Actor, subjectA, Now.AddMinutes(1));
        profile.RecordAssignmentChange(ids.NewId(), AccessProfileChangeKind.Assigned, Actor, subjectB, Now.AddMinutes(2));
        await dbContext.SaveChangesAsync();

        AccessControlPage<AccessProfileAssignmentDetails> assignments = await repository.ListAssignmentsAsync(
            profile.Id, tenantA, PageRequest.Normalize(1, 1), CancellationToken.None);
        AccessControlPage<AccessProfileAssignmentDetails> wrongScopeAssignments = await repository.ListAssignmentsAsync(
            profile.Id, tenantB, PageRequest.Normalize(1, 10), CancellationToken.None);
        Assert.Single(assignments.Items);
        Assert.True(assignments.HasMore);
        Assert.Empty(wrongScopeAssignments.Items);
    }

    [Fact]
    public async Task Profile_history_is_scope_isolated_and_bounded()
    {
        await using AccessControlDbContext dbContext = CreateInMemoryDbContext();
        AccessProfileRepository repository = new(dbContext);
        AccessScope tenantA = AccessScope.Parse("tenant:tenant-a");
        AccessScope tenantB = AccessScope.Parse("tenant:tenant-b");
        AccessProfile profile = CreateProfile(tenantA, "front-desk");
        AccessSubject subjectA = AccessSubject.User("user-a");
        AccessSubject subjectB = AccessSubject.User("user-b");
        profile.RecordAssignmentChange(Guid.NewGuid(), AccessProfileChangeKind.Assigned, Actor, subjectA, Now.AddMinutes(1));
        profile.RecordAssignmentChange(Guid.NewGuid(), AccessProfileChangeKind.Assigned, Actor, subjectB, Now.AddMinutes(2));
        repository.Add(profile);
        await dbContext.SaveChangesAsync();

        AccessControlPage<AccessProfileChangeDetails> history = await repository.ListChangesAsync(
            profile.Id, tenantA, PageRequest.Normalize(1, 2), CancellationToken.None);
        AccessControlPage<AccessProfileChangeDetails> wrongScopeHistory = await repository.ListChangesAsync(
            profile.Id, tenantB, PageRequest.Normalize(1, 10), CancellationToken.None);

        Assert.Equal(2, history.Items.Count);
        Assert.True(history.HasMore);
        Assert.Equal(
            ["assigned", "assigned"],
            history.Items.Select(change => change.Kind));
        Assert.Empty(wrongScopeHistory.Items);
    }

    [Fact]
    public async Task Active_profile_grant_uses_scope_policy_and_archive_revokes_immediately()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using AccessControlDbContext dbContext = await CreateDbContextAsync(connection);
        const string permissionCode = "reservations.read";
        SequenceIdGenerator ids = new();
        AccessControlRbacRepository rbac = CreateRbacRepository(
            dbContext,
            ids,
            (permissionCode, new AccessScopeMatchOptions(AllowAncestorScopeGrants: true)));
        AccessProfileRepository profiles = new(dbContext);
        AccessScope tenantScope = AccessScope.Parse("tenant:tenant-a");
        AccessScope propertyScope = AccessScope.Parse("tenant:tenant-a/property:property-a");
        AccessScope otherTenantScope = AccessScope.Parse("tenant:tenant-b/property:property-a");
        AccessSubject subject = AccessSubject.User("user-a");
        AccessProfile profile = CreateProfile(tenantScope, "front-desk", [permissionCode]);
        await rbac.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        profiles.Add(profile);
        profiles.AddAssignment(CreateAssignment(profile.Id, subject, ids.NewId()));
        profile.RecordAssignmentChange(ids.NewId(), AccessProfileChangeKind.Assigned, Actor, subject, Now);
        await dbContext.SaveChangesAsync();

        bool exactAllowed = await rbac.HasPermissionAsync(
            subject, PermissionCode.Create(permissionCode), tenantScope, CancellationToken.None);
        bool descendantAllowed = await rbac.HasPermissionAsync(
            subject, PermissionCode.Create(permissionCode), propertyScope, CancellationToken.None);
        bool otherTenantDenied = await rbac.HasPermissionAsync(
            subject, PermissionCode.Create(permissionCode), otherTenantScope, CancellationToken.None);

        long persistedVersion = await dbContext.AccessProfiles.AsNoTracking()
            .Where(candidate => candidate.Id == profile.Id)
            .Select(candidate => candidate.Version)
            .SingleAsync();
        Assert.Equal(1, persistedVersion);
        Assert.Equal(1, dbContext.Entry(profile).OriginalValues.GetValue<long>(nameof(AccessProfile.Version)));
        Result archived = profile.Archive(1, Actor, ids.NewId(), Now.AddMinutes(1));
        Assert.True(archived.IsSuccess);
        Assert.Equal(2, dbContext.Entry(profile).CurrentValues.GetValue<long>(nameof(AccessProfile.Version)));
        await dbContext.SaveChangesAsync();
        bool afterArchive = await rbac.HasPermissionAsync(
            subject, PermissionCode.Create(permissionCode), tenantScope, CancellationToken.None);

        Assert.True(exactAllowed);
        Assert.True(descendantAllowed);
        Assert.False(otherTenantDenied);
        Assert.False(afterArchive);
    }

    private static AccessProfile CreateProfile(
        AccessScope scope,
        string key,
        IReadOnlyCollection<string>? permissions = null)
    {
        Result<AccessProfile> result = AccessProfile.Create(
            Guid.NewGuid(), scope, key, key, null, permissions ?? [], Actor, Guid.NewGuid(), Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AccessProfileAssignment CreateAssignment(
        Guid profileId,
        AccessSubject subject,
        Guid id)
    {
        Result<AccessProfileAssignment> result = AccessProfileAssignment.Create(
            id, profileId, subject, Actor, Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<AccessControlDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite(connection)
            .Options;
        AccessControlDbContext dbContext = new(options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }

    private static AccessControlDbContext CreateInMemoryDbContext()
    {
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AccessControlDbContext(options);
    }

    private static AccessControlRbacRepository CreateRbacRepository(
        AccessControlDbContext dbContext,
        IIdGenerator ids,
        params (string Permission, AccessScopeMatchOptions Options)[] configuredPermissions) =>
        new(dbContext, ids, new TestScopeMatchOptionsResolver(configuredPermissions));

    private sealed class TestScopeMatchOptionsResolver(
        IEnumerable<(string Permission, AccessScopeMatchOptions Options)> configuredPermissions)
        : IAccessScopeMatchOptionsResolver
    {
        private readonly Dictionary<string, AccessScopeMatchOptions> configured = configuredPermissions
            .ToDictionary(item => item.Permission, item => item.Options, StringComparer.Ordinal);

        public AccessScopeMatchOptions Resolve(PermissionCode permission) =>
            this.configured.TryGetValue(permission.Value, out AccessScopeMatchOptions? options)
                ? options
                : new AccessScopeMatchOptions();
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        private int value;

        public Guid NewId()
        {
            this.value++;
            return Guid.Parse($"00000000-0000-0000-0000-{this.value:000000000000}");
        }
    }
}
