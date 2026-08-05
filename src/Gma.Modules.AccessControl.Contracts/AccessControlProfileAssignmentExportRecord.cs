namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessControlProfileAssignmentExportRecord(
    Guid AssignmentId,
    Guid ProfileId,
    string ProfileOwnerScopeValue,
    string ProfileKey,
    string AssignmentScopeValue,
    AccessSubjectKind SubjectKind,
    string SubjectId,
    AccessSubjectKind CreatedByKind,
    string CreatedById,
    DateTimeOffset CreatedAtUtc)
    : AccessControlScopeExportRecord;
