namespace Gma.Modules.AccessControl.Contracts;

public static class AccessControlAdminPermissionCodes
{
    public const string Bootstrap = AccessControlModuleMetadata.AdminSurfaceName + ".bootstrap";
    public const string RolesRead = AccessControlModuleMetadata.AdminSurfaceName + ".roles.read";
    public const string RolesManage = AccessControlModuleMetadata.AdminSurfaceName + ".roles.manage";
}
