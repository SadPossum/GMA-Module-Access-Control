namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Framework.Runtime.Identity;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Entities;
using Gma.Modules.AccessControl.Persistence.Repositories;
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

    private static AccessControlRbacRepository CreateRepository(AccessControlDbContext dbContext) =>
        new(dbContext, new SequenceIdGenerator());

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
