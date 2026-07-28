namespace Gma.Modules.AccessControl.Application.Ports;

internal enum AccessControlRoleAssignmentPersistenceOutcome
{
    Assigned = 0,
    AlreadyExists = 1,
    RoleDefinitionChanged = 2
}
