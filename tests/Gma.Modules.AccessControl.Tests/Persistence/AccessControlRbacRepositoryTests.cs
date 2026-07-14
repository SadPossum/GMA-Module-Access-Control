namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Framework.Runtime.Identity;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Entities;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessControlRbacRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Global_owner_wildcard_authorizes_any_permission_in_tenant_scope()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject subject = AccessSubject.AdminActor("owner-actor");

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("owner", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("owner", AccessControlPermissionGrant.OwnerWildcard, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "owner", AccessScope.Global, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        bool allowed = await repository.HasPermissionAsync(
            subject,
            PermissionCode.Create("catalog.items.discontinue"),
            AccessScope.Parse("tenant:tenant-a"),
            CancellationToken.None);

        Assert.True(allowed);
    }

    [Fact]
    public async Task Tenant_assignment_does_not_authorize_another_tenant()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject subject = AccessSubject.User("user-a");

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("catalog-reader", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("catalog-reader", "catalog.items.read", Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "catalog-reader", AccessScope.Parse("tenant:tenant-a"), Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        bool tenantA = await repository.HasPermissionAsync(
            subject,
            PermissionCode.Create("catalog.items.read"),
            AccessScope.Parse("tenant:tenant-a"),
            CancellationToken.None);
        bool tenantB = await repository.HasPermissionAsync(
            subject,
            PermissionCode.Create("catalog.items.read"),
            AccessScope.Parse("tenant:tenant-b"),
            CancellationToken.None);

        Assert.True(tenantA);
        Assert.False(tenantB);
    }

    [Fact]
    public async Task Global_concrete_permission_does_not_authorize_tenant_scope()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject subject = AccessSubject.AdminActor("operator-a");

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("catalog-operator", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("catalog-operator", "catalog.items.update", Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "catalog-operator", AccessScope.Global, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        bool allowed = await repository.HasPermissionAsync(
            subject,
            PermissionCode.Create("catalog.items.update"),
            AccessScope.Parse("tenant:tenant-a"),
            CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task Concrete_permission_grants_require_exact_scope()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject subject = AccessSubject.User("user-a");
        AccessScope tenantScope = AccessScope.Parse("tenant:tenant-a");
        AccessScope propertyScope = AccessScope.Create(
            AccessScopeSegment.Create("tenant", "tenant-a"),
            AccessScopeSegment.Create("property", "property-a"));

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("property-manager", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("property-manager", "properties.rooms.manage", Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "property-manager", tenantScope, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        bool allowed = await repository.HasPermissionAsync(
            subject,
            PermissionCode.Create("properties.rooms.manage"),
            propertyScope,
            CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task Descriptor_enabled_concrete_permission_grant_authorizes_descendants_but_not_other_tenants()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        PermissionCode permission = PermissionCode.Create("properties.rooms.manage");
        AccessControlRbacRepository repository = CreateRepository(
            dbContext,
            (permission.Value, new AccessScopeMatchOptions(AllowAncestorScopeGrants: true)));
        AccessSubject subject = AccessSubject.User("user-a");
        AccessScope tenantScope = AccessScope.Parse("tenant:tenant-a");
        AccessScope propertyScope = AccessScope.Parse("tenant:tenant-a/property:property-a");

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("property-manager", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("property-manager", permission.Value, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "property-manager", tenantScope, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        bool propertyAllowed = await repository.HasPermissionAsync(subject, permission, propertyScope, CancellationToken.None);
        bool otherTenantDenied = await repository.HasPermissionAsync(
            subject,
            permission,
            AccessScope.Parse("tenant:tenant-b/property:property-a"),
            CancellationToken.None);
        AccessGrantScope grant = Assert.Single(await repository.ListGrantedScopesAsync(
            subject,
            permission,
            CancellationToken.None));

        Assert.True(propertyAllowed);
        Assert.False(otherTenantDenied);
        Assert.True(grant.Grants(propertyScope));
        Assert.False(grant.Grants(AccessScope.Parse("tenant:tenant-b/property:property-a")));
    }

    [Fact]
    public async Task Descriptor_enabled_global_concrete_permission_grant_authorizes_descendants_consistently()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        PermissionCode permission = PermissionCode.Create("properties.portfolio.read");
        AccessControlRbacRepository repository = CreateRepository(
            dbContext,
            (permission.Value, new AccessScopeMatchOptions(
                AllowAncestorScopeGrants: true,
                AllowGlobalScopeGrant: true)));
        AccessSubject subject = AccessSubject.User("portfolio-user");
        AccessScope propertyScope = AccessScope.Parse("tenant:tenant-a/property:property-a");

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("portfolio-reader", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("portfolio-reader", permission.Value, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "portfolio-reader", AccessScope.Global, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        bool allowed = await repository.HasPermissionAsync(subject, permission, propertyScope, CancellationToken.None);
        AccessGrantScope grant = Assert.Single(await repository.ListGrantedScopesAsync(
            subject,
            permission,
            CancellationToken.None));

        Assert.True(allowed);
        Assert.True(grant.Grants(propertyScope));
        Assert.True(grant.MatchOptions.AllowGlobalScopeGrant);
    }

    [Fact]
    public async Task Owner_wildcard_ancestor_scope_authorizes_descendant_request()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject subject = AccessSubject.AdminActor("owner-a");
        AccessScope tenantScope = AccessScope.Parse("tenant:tenant-a");
        AccessScope propertyScope = AccessScope.Create(
            AccessScopeSegment.Create("tenant", "tenant-a"),
            AccessScopeSegment.Create("property", "property-a"));

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("tenant-owner", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("tenant-owner", AccessControlPermissionGrant.OwnerWildcard, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "tenant-owner", tenantScope, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        bool allowed = await repository.HasPermissionAsync(
            subject,
            PermissionCode.Create("properties.rooms.manage"),
            propertyScope,
            CancellationToken.None);

        Assert.True(allowed);
    }

    [Fact]
    public async Task Equivalent_scope_value_does_not_create_duplicate_assignment()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject subject = AccessSubject.User("user-a");

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("catalog-reader", Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "catalog-reader", AccessScope.Parse("tenant:tenant-a"), Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "catalog-reader", AccessScope.Parse("tenant:tenant-a"), Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Single(dbContext.SubjectRoleAssignments);
    }

    [Fact]
    public async Task List_roles_returns_permissions_and_assignment_count()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject subject = AccessSubject.AdminActor("actor-a");

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("operators", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("operators", "auth.members.read", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("operators", "auth.members.disable", Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "operators", AccessScope.Global, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        AccessControlRoleDetails role = Assert.Single(await repository.ListRolesAsync(CancellationToken.None));

        Assert.Equal("operators", role.Name);
        Assert.Equal(["auth.members.disable", "auth.members.read"], role.Permissions);
        Assert.Equal(1, role.AssignmentCount);
    }

    [Fact]
    public async Task List_role_assignments_preserves_subject_kind_identity_for_same_subject_id()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject user = AccessSubject.User("member-a");
        AccessSubject admin = AccessSubject.AdminActor("member-a");

        await repository.EnsureSubjectAsync(user, Now, CancellationToken.None);
        await repository.EnsureSubjectAsync(admin, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("property-reader", Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(user, "property-reader", AccessScope.Parse("tenant:tenant-a"), Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(admin, "property-reader", AccessScope.Global, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        IReadOnlyList<AccessControlRoleAssignmentDetails> assignments = await repository
            .ListRoleAssignmentsAsync("property-reader", CancellationToken.None);

        Assert.Equal(2, assignments.Count);
        Assert.Contains(assignments, assignment => assignment.SubjectKind == AccessSubjectKind.User && assignment.SubjectId == "member-a");
        Assert.Contains(assignments, assignment => assignment.SubjectKind == AccessSubjectKind.AdminActor && assignment.SubjectId == "member-a");
    }

    [Fact]
    public async Task Revoke_permission_takes_effect_immediately_and_reports_missing_grant()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject subject = AccessSubject.User("user-a");
        PermissionCode permission = PermissionCode.Create("properties.read");
        AccessScope scope = AccessScope.Parse("tenant:tenant-a");

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("property-reader", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("property-reader", permission.Value, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "property-reader", scope, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.True(await repository.HasPermissionAsync(subject, permission, scope, CancellationToken.None));
        Assert.Equal(
            AccessControlRemovalOutcome.Removed,
            await repository.RevokeRolePermissionAsync("property-reader", permission.Value, CancellationToken.None));
        Assert.False(await repository.HasPermissionAsync(subject, permission, scope, CancellationToken.None));
        Assert.Equal(
            AccessControlRemovalOutcome.NotFound,
            await repository.RevokeRolePermissionAsync("property-reader", permission.Value, CancellationToken.None));
    }

    [Fact]
    public async Task Unassign_role_removes_only_the_exact_subject_kind_and_scope()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject user = AccessSubject.User("member-a");
        AccessSubject admin = AccessSubject.AdminActor("member-a");
        PermissionCode permission = PermissionCode.Create("properties.read");
        AccessScope tenantA = AccessScope.Parse("tenant:tenant-a");
        AccessScope tenantB = AccessScope.Parse("tenant:tenant-b");

        await repository.EnsureSubjectAsync(user, Now, CancellationToken.None);
        await repository.EnsureSubjectAsync(admin, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("property-reader", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("property-reader", permission.Value, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(user, "property-reader", tenantA, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(user, "property-reader", tenantB, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(admin, "property-reader", tenantA, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(
            AccessControlRemovalOutcome.Removed,
            await repository.UnassignRoleAsync(user, "property-reader", tenantA, CancellationToken.None));

        IReadOnlyList<AccessControlRoleAssignmentDetails> assignments = await repository
            .ListRoleAssignmentsAsync("property-reader", CancellationToken.None);
        Assert.DoesNotContain(assignments, assignment =>
            assignment.SubjectKind == AccessSubjectKind.User && assignment.AccessScope.Equals(tenantA));
        Assert.Contains(assignments, assignment =>
            assignment.SubjectKind == AccessSubjectKind.User && assignment.AccessScope.Equals(tenantB));
        Assert.Contains(assignments, assignment =>
            assignment.SubjectKind == AccessSubjectKind.AdminActor && assignment.AccessScope.Equals(tenantA));
        Assert.Equal(
            AccessControlRemovalOutcome.NotFound,
            await repository.UnassignRoleAsync(user, "property-reader", tenantA, CancellationToken.None));
    }

    [Fact]
    public async Task Unassign_role_participates_in_an_existing_relational_transaction()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite(connection)
            .Options;
        await using AccessControlDbContext dbContext = new(options);
        await dbContext.Database.EnsureCreatedAsync();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject subject = AccessSubject.User("member-a");
        AccessScope scope = AccessScope.Parse("tenant:tenant-a");

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("workspace-member", Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(
            subject,
            "workspace-member",
            scope,
            Now,
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync();

        AccessControlRemovalOutcome removed = await repository.UnassignRoleAsync(
            subject,
            "workspace-member",
            scope,
            CancellationToken.None);
        AccessControlRemovalOutcome missing = await repository.UnassignRoleAsync(
            subject,
            "workspace-member",
            scope,
            CancellationToken.None);

        Assert.Equal(AccessControlRemovalOutcome.Removed, removed);
        Assert.Equal(AccessControlRemovalOutcome.NotFound, missing);
        Assert.Same(transaction, dbContext.Database.CurrentTransaction);
        await transaction.CommitAsync();

        Assert.False(await repository.AssignmentExistsAsync(
            subject,
            "workspace-member",
            scope,
            CancellationToken.None));
    }

    [Fact]
    public async Task Final_global_admin_owner_is_protected_from_unassignment_and_wildcard_revocation()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject owner = AccessSubject.AdminActor("owner-a");
        AccessSubject userWithWildcard = AccessSubject.User("user-a");

        await repository.EnsureSubjectAsync(owner, Now, CancellationToken.None);
        await repository.EnsureSubjectAsync(userWithWildcard, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("owner", Now, CancellationToken.None);
        await repository.EnsureRoleAsync("user-owner", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("owner", AccessControlPermissionGrant.OwnerWildcard, Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("user-owner", AccessControlPermissionGrant.OwnerWildcard, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(owner, "owner", AccessScope.Global, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(userWithWildcard, "user-owner", AccessScope.Global, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(
            AccessControlRemovalOutcome.LastOwnerProtected,
            await repository.UnassignRoleAsync(owner, "owner", AccessScope.Global, CancellationToken.None));
        Assert.Equal(
            AccessControlRemovalOutcome.LastOwnerProtected,
            await repository.RevokeRolePermissionAsync("owner", AccessControlPermissionGrant.OwnerWildcard, CancellationToken.None));
    }

    [Fact]
    public async Task One_global_admin_owner_can_be_removed_when_another_remains()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject ownerA = AccessSubject.AdminActor("owner-a");
        AccessSubject ownerB = AccessSubject.AdminActor("owner-b");

        await repository.EnsureSubjectAsync(ownerA, Now, CancellationToken.None);
        await repository.EnsureSubjectAsync(ownerB, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("owner-a", Now, CancellationToken.None);
        await repository.EnsureRoleAsync("owner-b", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("owner-a", AccessControlPermissionGrant.OwnerWildcard, Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("owner-b", AccessControlPermissionGrant.OwnerWildcard, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(ownerA, "owner-a", AccessScope.Global, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(ownerB, "owner-b", AccessScope.Global, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(
            AccessControlRemovalOutcome.Removed,
            await repository.UnassignRoleAsync(ownerA, "owner-a", AccessScope.Global, CancellationToken.None));
        Assert.Equal(
            AccessControlRemovalOutcome.LastOwnerProtected,
            await repository.RevokeRolePermissionAsync("owner-b", AccessControlPermissionGrant.OwnerWildcard, CancellationToken.None));
    }

    [Fact]
    public async Task List_granted_scopes_returns_concrete_and_owner_scopes_for_permission()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        AccessSubject subject = AccessSubject.User("user-a");
        AccessScope tenantScope = AccessScope.Parse("tenant:tenant-a");
        AccessScope propertyScope = AccessScope.Parse("tenant:tenant-a/property:property-a");

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("catalog-reader", Now, CancellationToken.None);
        await repository.EnsureRoleAsync("tenant-owner", Now, CancellationToken.None);
        await repository.EnsureRoleAsync("irrelevant", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("catalog-reader", "catalog.items.read", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("tenant-owner", AccessControlPermissionGrant.OwnerWildcard, Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("irrelevant", "orders.read", Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "catalog-reader", propertyScope, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "tenant-owner", tenantScope, Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "irrelevant", AccessScope.Parse("tenant:tenant-b"), Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        IReadOnlyList<AccessGrantScope> scopes = await repository.ListGrantedScopesAsync(
            subject,
            PermissionCode.Create("catalog.items.read"),
            CancellationToken.None);

        Assert.Equal(2, scopes.Count);
        AccessGrantScope concrete = Assert.Single(scopes, scope => scope.Scope.Equals(propertyScope));
        AccessGrantScope owner = Assert.Single(scopes, scope => scope.Scope.Equals(tenantScope));
        Assert.True(concrete.Grants(propertyScope));
        Assert.False(concrete.Grants(AccessScope.Parse("tenant:tenant-a/property:property-b")));
        Assert.True(owner.Grants(propertyScope));
    }

    [Fact]
    public void Permission_check_candidate_filters_translate_to_sql()
    {
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlServer("Server=(local);Database=gma_access_control_translation;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using AccessControlDbContext dbContext = new(options);
        string[] candidateScopeValues =
        [
            AccessScope.Parse("tenant:tenant-a/property:property-a").Value,
            AccessScope.Parse("tenant:tenant-a").Value,
            AccessScope.Global.Value
        ];
        string[] candidatePermissionCodes =
        [
            PermissionCode.Create("catalog.items.read").Value,
            AccessControlPermissionGrant.OwnerWildcard
        ];

        IQueryable<AccessSubjectRoleAssignment> assignments = dbContext.SubjectRoleAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.SubjectKind == (int)AccessSubjectKind.User &&
                assignment.SubjectId == "user-a" &&
                candidateScopeValues.Contains(assignment.ScopeValue));

        IQueryable<AccessRolePermission> permissionGrants = dbContext.RolePermissions
            .AsNoTracking()
            .Where(permissionGrant => candidatePermissionCodes.Contains(permissionGrant.PermissionCode));

        string sql = assignments
            .Join(
                permissionGrants,
                assignment => assignment.RoleId,
                permissionGrant => permissionGrant.RoleId,
                (assignment, permissionGrant) => new
                {
                    assignment.SubjectKind,
                    assignment.SubjectId,
                    assignment.ScopeValue,
                    permissionGrant.PermissionCode
                })
            .ToQueryString();

        Assert.Contains("[s].[Scope] IN", sql, StringComparison.Ordinal);
        Assert.Contains("[r].[PermissionCode] IN", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decision_provider_abstains_when_no_grant_matches()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        PersistedAccessControlDecisionProvider provider = new(repository);

        AccessDecision decision = await provider.DecideAsync(
            new AccessRequirement(
                AccessSubject.User("user-a"),
                PermissionCode.Create("catalog.items.read"),
                AccessScope.Parse("tenant:tenant-a")),
            CancellationToken.None);

        Assert.True(decision.IsAbstain);
    }

    [Fact]
    public async Task Decision_provider_allows_when_grant_matches()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = CreateRepository(dbContext);
        PersistedAccessControlDecisionProvider provider = new(repository);
        AccessSubject subject = AccessSubject.User("user-a");

        await repository.EnsureSubjectAsync(subject, Now, CancellationToken.None);
        await repository.EnsureRoleAsync("catalog-reader", Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync("catalog-reader", "catalog.items.read", Now, CancellationToken.None);
        await repository.EnsureRoleAssignmentAsync(subject, "catalog-reader", AccessScope.Parse("tenant:tenant-a"), Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        AccessDecision decision = await provider.DecideAsync(
            new AccessRequirement(subject, PermissionCode.Create("catalog.items.read"), AccessScope.Parse("tenant:tenant-a")),
            CancellationToken.None);

        Assert.True(decision.IsAllowed);
    }

    private static AccessControlDbContext CreateDbContext()
    {
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AccessControlDbContext(options);
    }

    private static AccessControlRbacRepository CreateRepository(
        AccessControlDbContext dbContext,
        params (string Permission, AccessScopeMatchOptions Options)[] configuredPermissions) =>
        new(dbContext, new SequenceIdGenerator(), new TestScopeMatchOptionsResolver(configuredPermissions));

    private sealed class TestScopeMatchOptionsResolver(
        IEnumerable<(string Permission, AccessScopeMatchOptions Options)> configuredPermissions)
        : IAccessScopeMatchOptionsResolver
    {
        private readonly Dictionary<string, AccessScopeMatchOptions> optionsByPermission = configuredPermissions
            .ToDictionary(item => item.Permission, item => item.Options, StringComparer.Ordinal);

        public AccessScopeMatchOptions Resolve(PermissionCode permission) =>
            this.optionsByPermission.TryGetValue(permission.Value, out AccessScopeMatchOptions? options)
                ? options
                : new AccessScopeMatchOptions();
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        private int next;

        public Guid NewId()
        {
            this.next++;
            return Guid.Parse($"00000000-0000-0000-0000-{this.next:000000000000}");
        }
    }
}
