namespace Gma.Modules.AccessControl.Contracts;

public enum AccessRoleAssignmentLifecycleStage
{
    Unknown = 0,
    Requested = 1,
    Granted = 2,
    Denied = 3,
    Revoked = 4
}
