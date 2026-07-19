namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessControlRoleDefinition(
    string Name,
    IReadOnlyCollection<string> Permissions);
