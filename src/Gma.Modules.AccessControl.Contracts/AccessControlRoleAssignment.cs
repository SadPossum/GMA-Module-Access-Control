namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessControlRoleAssignment(
    Guid Id,
    AccessSubjectKind SubjectKind,
    string SubjectId,
    string RoleName,
    AccessScope Scope,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc = null,
    DateTimeOffset? RevokedAtUtc = null,
    AccessRoleAssignmentStatus Status = AccessRoleAssignmentStatus.Active);
