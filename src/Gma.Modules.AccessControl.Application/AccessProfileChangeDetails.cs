namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;

public sealed record AccessProfileChangeDetails(
    Guid Id,
    Guid ProfileId,
    string Kind,
    AccessSubjectKind ActorKind,
    string ActorId,
    AccessSubjectKind? SubjectKind,
    string? SubjectId,
    long ProfileVersion,
    DateTimeOffset OccurredAtUtc);
