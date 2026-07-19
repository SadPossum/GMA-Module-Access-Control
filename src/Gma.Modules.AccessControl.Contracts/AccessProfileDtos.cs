namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessProfileDto(
    Guid Id,
    string OwnerScope,
    string Key,
    string DisplayName,
    string Description,
    string Status,
    long Version,
    IReadOnlyList<string> Permissions,
    int AssignmentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastChangedAtUtc);

public sealed record AccessProfileAssignmentDto(
    Guid Id,
    Guid ProfileId,
    string SubjectKind,
    string SubjectId,
    string CreatedByKind,
    string CreatedById,
    DateTimeOffset CreatedAtUtc);

public sealed record AccessProfileChangeDto(
    Guid Id,
    Guid ProfileId,
    string Kind,
    string ActorKind,
    string ActorId,
    string? SubjectKind,
    string? SubjectId,
    long ProfileVersion,
    DateTimeOffset OccurredAtUtc);
