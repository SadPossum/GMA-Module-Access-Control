namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessProfileChangeDto(
    Guid Id,
    Guid ProfileId,
    AccessProfileChangeKind Kind,
    string ActorKind,
    string ActorId,
    string? SubjectKind,
    string? SubjectId,
    long ProfileVersion,
    DateTimeOffset OccurredAtUtc,
    string? AssignmentScope);
