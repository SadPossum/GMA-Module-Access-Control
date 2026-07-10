namespace Gma.Modules.AccessControl.Admin.Contracts;

using Gma.Modules.AccessControl.Contracts;

public static class AccessControlAdminOperationNames
{
    public const string Bootstrap = AccessControlModuleMetadata.AdminSurfaceName + ".bootstrap";
    public const string RolesList = AccessControlModuleMetadata.AdminSurfaceName + ".roles.list";
    public const string RolesCreate = AccessControlModuleMetadata.AdminSurfaceName + ".roles.create";
    public const string RolesGrant = AccessControlModuleMetadata.AdminSurfaceName + ".roles.grant";
    public const string RolesAssign = AccessControlModuleMetadata.AdminSurfaceName + ".roles.assign";
}
