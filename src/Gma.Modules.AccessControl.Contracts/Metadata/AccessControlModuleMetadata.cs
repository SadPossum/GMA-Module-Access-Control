namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.Permissions;
using Gma.Framework.Modules;
using Gma.Framework.ModuleComposition;

public static class AccessControlModuleMetadata
{
    public const string Name = "access-control";
    public const string Schema = "access";
    public const string AdminSurfaceName = "admin";

    public static ModuleDescriptor Descriptor { get; } = ModuleDescriptor
        .Create(Name)
        .WithSchema(Schema)
        .WithAdminSurfaceName(AdminSurfaceName)
        .WithProfile(AccessControlProfiles.Default)
        .WithPermissions([
            new ModulePermissionDescriptor(AccessControlAdminPermissionCodes.Bootstrap, "Bootstrap the first access-control owner.", scopeRequirement: PermissionScopeRequirement.Global),
            new ModulePermissionDescriptor(AccessControlAdminPermissionCodes.RolesRead, "Read access-control roles.", scopeRequirement: PermissionScopeRequirement.Global),
            new ModulePermissionDescriptor(AccessControlAdminPermissionCodes.RolesManage, "Manage access-control roles and assignments.", scopeRequirement: PermissionScopeRequirement.Global),
        ])
        .Build();
}
