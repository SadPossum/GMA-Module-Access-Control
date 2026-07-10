namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;

public sealed record AccessControlRoleAssignmentDetails(
    Guid Id,
    AccessSubjectKind SubjectKind,
    string SubjectId,
    string RoleName,
    AccessScope AccessScope,
    DateTimeOffset CreatedAtUtc);
