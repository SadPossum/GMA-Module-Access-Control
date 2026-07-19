namespace Gma.Modules.AccessControl.Contracts;

public enum AccessControlAssignmentRemovalOutcome
{
    Unknown = 0,
    Removed = 1,
    NotFound = 2,
    LastOwnerProtected = 3
}
