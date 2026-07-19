namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessProfileDto(
    Guid Id,
    string OwnerScope,
    string Key,
    string DisplayName,
    string Description,
    AccessProfileStatus Status,
    long Version,
    IReadOnlyList<string> Permissions,
    int AssignmentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastChangedAtUtc);
