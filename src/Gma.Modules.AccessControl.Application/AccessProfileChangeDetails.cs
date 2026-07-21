namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Contracts;

public sealed record AccessProfileChangeDetails(
    Guid Id,
    Guid ProfileId,
    AccessProfileChangeKind Kind,
    AccessSubjectKind ActorKind,
    string ActorId,
    AccessSubjectKind? SubjectKind,
    string? SubjectId,
    long ProfileVersion,
    DateTimeOffset OccurredAtUtc,
    AccessScope? AssignmentScope);
