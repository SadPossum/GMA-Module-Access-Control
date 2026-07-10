namespace Gma.Modules.AccessControl.Persistence.Entities;

using Gma.Framework.AccessControl;

public sealed class AccessSubjectRoleAssignment
{
    private AccessSubjectRoleAssignment() { }

    public AccessSubjectRoleAssignment(
        Guid id,
        AccessSubject subject,
        Guid roleId,
        AccessScope scope,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);

        this.Id = id;
        this.SubjectKind = (int)subject.Kind;
        this.SubjectId = subject.Id;
        this.RoleId = roleId;
        this.ScopeValue = scope.Value;
        this.CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public int SubjectKind { get; private set; }
    public string SubjectId { get; private set; } = string.Empty;
    public Guid RoleId { get; private set; }
    public string ScopeValue { get; private set; } = AccessScope.Global.Value;
    public AccessScope Scope => AccessScope.Parse(this.ScopeValue);
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public AccessRole? Role { get; private set; }
}
