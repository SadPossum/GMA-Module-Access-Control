namespace Gma.Modules.AccessControl.Contracts;

public static class AccessControlProfilePermissionCodes
{
    public const string Read = AccessControlModuleMetadata.Name + ".profiles.read";
    public const string Manage = AccessControlModuleMetadata.Name + ".profiles.manage";
    public const string Assign = AccessControlModuleMetadata.Name + ".profiles.assign";
}
