namespace Gma.Modules.AccessControl.Persistence.Entities;

using Gma.Framework.AccessControl;

public sealed class AccessPrincipal
{
    private AccessPrincipal() { }

    public AccessPrincipal(AccessSubject subject, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);

        this.Kind = (int)subject.Kind;
        this.SubjectId = subject.Id;
        this.CreatedAtUtc = createdAtUtc;
    }

    public int Kind { get; private set; }
    public string SubjectId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
