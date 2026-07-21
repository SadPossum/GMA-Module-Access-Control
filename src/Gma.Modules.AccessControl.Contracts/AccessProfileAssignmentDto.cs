namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessProfileAssignmentDto(
    Guid Id,
    Guid ProfileId,
    string SubjectKind,
    string SubjectId,
    string CreatedByKind,
    string CreatedById,
    DateTimeOffset CreatedAtUtc,
    string AssignmentScope);
