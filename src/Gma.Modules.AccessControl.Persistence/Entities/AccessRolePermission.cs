namespace Gma.Modules.AccessControl.Persistence.Entities;

using System.Diagnostics.CodeAnalysis;
using Gma.Modules.AccessControl.Application;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Role permission is the persisted RBAC concept name.")]
public sealed class AccessRolePermission
{
    private AccessRolePermission() { }

    public AccessRolePermission(Guid id, Guid roleId, string permissionCode, DateTimeOffset createdAtUtc)
    {
        this.Id = id;
        this.RoleId = roleId;
        this.PermissionCode = AccessControlPermissionGrant.Normalize(permissionCode);
        this.CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid RoleId { get; private set; }
    public string PermissionCode { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public AccessRole? Role { get; private set; }
}
