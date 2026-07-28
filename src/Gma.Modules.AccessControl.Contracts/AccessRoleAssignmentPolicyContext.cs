namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed class AccessRoleAssignmentPolicyContext
{
    public AccessRoleAssignmentPolicyContext(
        AccessSubject subject,
        string roleName,
        AccessScope accessScope,
        DateTimeOffset? expiresAtUtc,
        IReadOnlyCollection<string>? permissions = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        ArgumentNullException.ThrowIfNull(accessScope);

        this.Subject = subject;
        this.RoleName = roleName;
        this.AccessScope = accessScope;
        this.ExpiresAtUtc = expiresAtUtc?.ToUniversalTime();
        this.Permissions = permissions?.ToArray() ?? [];
    }

    public AccessSubject Subject { get; }
    public string RoleName { get; }
    public AccessScope AccessScope { get; }
    public DateTimeOffset? ExpiresAtUtc { get; }
    public IReadOnlyList<string> Permissions { get; }
}
