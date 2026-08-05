namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessControlProfileChangeExportRecord(
    Guid ChangeId,
    Guid ProfileId,
    string ProfileOwnerScopeValue,
    string ProfileKey,
    AccessProfileChangeKind Kind,
    AccessSubjectKind ActorKind,
    string ActorId,
    AccessSubjectKind? SubjectKind,
    string? SubjectId,
    string? AssignmentScopeValue,
    long ProfileVersion,
    DateTimeOffset OccurredAtUtc)
    : AccessControlScopeExportRecord;
