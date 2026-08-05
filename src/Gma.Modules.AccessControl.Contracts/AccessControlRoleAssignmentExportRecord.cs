namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessControlRoleAssignmentExportRecord(
    Guid AssignmentId,
    AccessSubjectKind SubjectKind,
    string SubjectId,
    string RoleName,
    IReadOnlyList<string> RolePermissions,
    string AccessScopeValue,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc)
    : AccessControlScopeExportRecord;
