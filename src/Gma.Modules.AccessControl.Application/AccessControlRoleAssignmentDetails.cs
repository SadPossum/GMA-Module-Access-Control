namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Contracts;

public sealed record AccessControlRoleAssignmentDetails(
    Guid Id,
    AccessSubjectKind SubjectKind,
    string SubjectId,
    string RoleName,
    AccessScope AccessScope,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc = null,
    DateTimeOffset? RevokedAtUtc = null,
    AccessRoleAssignmentStatus Status = AccessRoleAssignmentStatus.Active);
