namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed class AccessProfileAssignmentPolicyContext
{
    public AccessProfileAssignmentPolicyContext(
        Guid profileId,
        string profileKey,
        AccessScope ownerScope,
        AccessSubject actor,
        AccessSubject subject,
        IReadOnlyCollection<string> permissions,
        AccessScope? assignmentScope = null)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("An access-profile id is required.", nameof(profileId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(profileKey);
        ArgumentNullException.ThrowIfNull(ownerScope);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(permissions);
        if (ownerScope.IsGlobal)
        {
            throw new ArgumentException("An access-profile assignment requires a non-global owner scope.", nameof(ownerScope));
        }

        this.ProfileId = profileId;
        this.ProfileKey = profileKey;
        this.OwnerScope = ownerScope;
        this.AssignmentScope = assignmentScope ?? ownerScope;
        this.Actor = actor;
        this.Subject = subject;
        this.Permissions = Array.AsReadOnly(permissions.ToArray());
    }

    public Guid ProfileId { get; }
    public string ProfileKey { get; }
    public AccessScope OwnerScope { get; }
    public AccessScope AssignmentScope { get; }
    public AccessSubject Actor { get; }
    public AccessSubject Subject { get; }
    public IReadOnlyList<string> Permissions { get; }
}
