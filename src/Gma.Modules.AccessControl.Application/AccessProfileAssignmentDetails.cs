namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;

public sealed record AccessProfileAssignmentDetails(
    Guid Id,
    Guid ProfileId,
    AccessSubjectKind SubjectKind,
    string SubjectId,
    AccessSubjectKind CreatedByKind,
    string CreatedById,
    DateTimeOffset CreatedAtUtc,
    AccessScope AssignmentScope);
