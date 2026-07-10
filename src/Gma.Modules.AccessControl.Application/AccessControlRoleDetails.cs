namespace Gma.Modules.AccessControl.Application;

public sealed record AccessControlRoleDetails(
    Guid Id,
    string Name,
    IReadOnlyCollection<string> Permissions,
    int AssignmentCount);
