namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessProfileDefinition(
    string Key,
    string DisplayName,
    string? Description,
    IReadOnlyCollection<string> Permissions);
