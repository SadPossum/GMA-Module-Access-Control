namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessControlAuthorityRoleGrant(
    Guid AssignmentId,
    Guid RoleId,
    string RoleName,
    IReadOnlyList<string> Permissions,
    AccessScope Scope,
    DateTimeOffset? ExpiresAtUtc);
