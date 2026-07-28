namespace Gma.Modules.AccessControl.Application.Ports;

internal enum AccessControlRolePermissionGrantPersistenceOutcome
{
    Granted = 0,
    AlreadyGranted = 1,
    TemporaryAssignmentsExist = 2
}
