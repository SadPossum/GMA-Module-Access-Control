namespace Gma.Modules.AccessControl.Admin.Contracts;

using Gma.Modules.AccessControl.Contracts;
using Gma.Framework.Administration;

public static class AccessControlAdminPermissions
{
    public static readonly AdminPermission Bootstrap = AdminPermission.Create(AccessControlAdminPermissionCodes.Bootstrap);
    public static readonly AdminPermission RolesRead = AdminPermission.Create(AccessControlAdminPermissionCodes.RolesRead);
    public static readonly AdminPermission RolesManage = AdminPermission.Create(AccessControlAdminPermissionCodes.RolesManage);
}
