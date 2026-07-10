namespace Gma.Modules.AccessControl.Application.Ports;

public enum AccessControlRemovalOutcome
{
    Unknown = 0,
    Removed = 1,
    NotFound = 2,
    LastOwnerProtected = 3
}
