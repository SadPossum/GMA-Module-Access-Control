namespace Gma.Modules.AccessControl.Persistence.Entities;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Persistence;

public sealed class AccessSubjectRoleAssignment
{
    private AccessSubjectRoleAssignment() { }

    public AccessSubjectRoleAssignment(
        Guid id,
        AccessSubject subject,
        Guid roleId,
        AccessScope scope,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);
        if (expiresAtUtc is not null &&
            expiresAtUtc.Value.ToUnixTimeMilliseconds() <= createdAtUtc.ToUnixTimeMilliseconds())
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAtUtc),
                expiresAtUtc,
                "Role-assignment expiry must be later than creation.");
        }

        this.Id = id;
        this.SubjectKind = (int)subject.Kind;
        this.SubjectId = subject.Id;
        this.RoleId = roleId;
        this.ScopeValue = scope.Value;
        this.ScopeHash = AccessScopeIndex.Create(scope.Value);
        this.CreatedAtUtc = createdAtUtc.ToUniversalTime();
        this.ExpiresAtUnixMilliseconds = expiresAtUtc?.ToUniversalTime().ToUnixTimeMilliseconds();
    }

    public Guid Id { get; private set; }
    public int SubjectKind { get; private set; }
    public string SubjectId { get; private set; } = string.Empty;
    public Guid RoleId { get; private set; }
    public string ScopeValue { get; private set; } = AccessScope.Global.Value;
    public string ScopeHash { get; private set; } = string.Empty;
    public AccessScope Scope => AccessScope.Parse(this.ScopeValue);
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public long? ExpiresAtUnixMilliseconds { get; private set; }
    public long? RevokedAtUnixMilliseconds { get; private set; }
    public DateTimeOffset? ExpiresAtUtc => FromUnixMilliseconds(this.ExpiresAtUnixMilliseconds);
    public DateTimeOffset? RevokedAtUtc => FromUnixMilliseconds(this.RevokedAtUnixMilliseconds);

    public AccessRole? Role { get; private set; }

    public bool IsActiveAt(DateTimeOffset nowUtc) =>
        this.RevokedAtUnixMilliseconds is null &&
        (this.ExpiresAtUnixMilliseconds is null ||
         this.ExpiresAtUnixMilliseconds > nowUtc.ToUnixTimeMilliseconds());

    public void Revoke(DateTimeOffset revokedAtUtc)
    {
        if (this.RevokedAtUnixMilliseconds is not null)
        {
            return;
        }

        DateTimeOffset normalized = revokedAtUtc.ToUniversalTime();
        if (normalized < this.CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revokedAtUtc),
                revokedAtUtc,
                "Revocation cannot predate the role assignment.");
        }

        this.RevokedAtUnixMilliseconds = normalized.ToUnixTimeMilliseconds();
    }

    private static DateTimeOffset? FromUnixMilliseconds(long? value) =>
        value is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(value.Value);
}
