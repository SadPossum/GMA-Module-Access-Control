namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Entities;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessControlRbacEntityTests
{
    private static readonly Guid Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid RoleId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Principal_persists_subject_kind_and_id()
    {
        AccessPrincipal principal = new(AccessSubject.User(" User-1 "), CreatedAtUtc);

        Assert.Equal((int)AccessSubjectKind.User, principal.Kind);
        Assert.Equal("User-1", principal.SubjectId);
        Assert.Equal(CreatedAtUtc, principal.CreatedAtUtc);
    }

    [Fact]
    public void Role_normalizes_role_name()
    {
        AccessRole role = new(Id, " Owner ", CreatedAtUtc);

        Assert.Equal("owner", role.Name);
        Assert.Equal(CreatedAtUtc, role.CreatedAtUtc);
    }

    [Fact]
    public void Role_permission_normalizes_permission_code()
    {
        AccessRolePermission permission = new(Id, RoleId, " Auth.Members.Read ", CreatedAtUtc);

        Assert.Equal("auth.members.read", permission.PermissionCode);
        Assert.Equal(RoleId, permission.RoleId);
    }

    [Fact]
    public void Role_permission_allows_owner_wildcard()
    {
        AccessRolePermission permission = new(Id, RoleId, " * ", CreatedAtUtc);

        Assert.Equal(AccessControlPermissionGrant.OwnerWildcard, permission.PermissionCode);
    }

    [Fact]
    public void Role_permission_rejects_invalid_permission_code()
    {
        Assert.Throws<ArgumentException>(() => new AccessRolePermission(Id, RoleId, "auth", CreatedAtUtc));
    }

    [Fact]
    public void Assignment_persists_subject_and_scope()
    {
        AccessSubject subject = AccessSubject.AdminActor(" Actor-1 ");
        AccessScope scope = AccessScope.Parse("tenant:tenant-a");

        AccessSubjectRoleAssignment assignment = new(Id, subject, RoleId, scope, CreatedAtUtc);

        Assert.Equal((int)AccessSubjectKind.AdminActor, assignment.SubjectKind);
        Assert.Equal("Actor-1", assignment.SubjectId);
        Assert.Equal(RoleId, assignment.RoleId);
        Assert.Equal("tenant:tenant-a", assignment.Scope.Value);
        Assert.Equal(AccessScopeIndex.HashLength, assignment.ScopeHash.Length);
        Assert.DoesNotContain("tenant-a", assignment.ScopeHash, StringComparison.Ordinal);
    }
}
