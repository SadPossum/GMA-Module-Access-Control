namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessProfileUpdate(
    string DisplayName,
    string? Description,
    IReadOnlyCollection<string> Permissions,
    long ExpectedVersion);
