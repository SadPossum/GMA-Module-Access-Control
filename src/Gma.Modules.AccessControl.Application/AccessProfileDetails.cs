namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;

public sealed record AccessProfileDetails(
    Guid Id,
    AccessScope OwnerScope,
    string Key,
    string DisplayName,
    string Description,
    string Status,
    long Version,
    IReadOnlyList<string> Permissions,
    int AssignmentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastChangedAtUtc);
