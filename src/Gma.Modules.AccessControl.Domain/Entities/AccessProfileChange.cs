namespace Gma.Modules.AccessControl.Domain.Entities;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.Aggregates;

public sealed class AccessProfileChange
{
    private AccessProfileChange() { }

    internal AccessProfileChange(
        Guid id,
        Guid profileId,
        AccessProfileChangeKind kind,
        AccessSubject actor,
        long profileVersion,
        DateTimeOffset occurredAtUtc,
        AccessSubject? assignmentSubject = null)
    {
        this.Id = id;
        this.ProfileId = profileId;
        this.Kind = kind;
        this.ActorKind = (int)actor.Kind;
        this.ActorId = actor.Id;
        this.SubjectKind = assignmentSubject is null ? null : (int)assignmentSubject.Kind;
        this.SubjectId = assignmentSubject?.Id;
        this.ProfileVersion = profileVersion;
        this.OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid ProfileId { get; private set; }
    public AccessProfileChangeKind Kind { get; private set; }
    public int ActorKind { get; private set; }
    public string ActorId { get; private set; } = string.Empty;
    public int? SubjectKind { get; private set; }
    public string? SubjectId { get; private set; }
    public long ProfileVersion { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public AccessProfile? Profile { get; private set; }
}
