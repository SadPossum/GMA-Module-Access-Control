namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessRoleAssignmentLifecycleEvent
{
    public AccessRoleAssignmentLifecycleEvent(
        AccessRoleAssignmentLifecycleStage stage,
        AccessSubject subject,
        string roleName,
        AccessScope accessScope,
        DateTimeOffset? expiresAtUtc,
        Guid correlationId)
    {
        if (stage is AccessRoleAssignmentLifecycleStage.Unknown || !Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "Role-assignment lifecycle stage is invalid.");
        }

        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        ArgumentNullException.ThrowIfNull(accessScope);
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Role-assignment lifecycle correlation id cannot be empty.",
                nameof(correlationId));
        }

        this.Stage = stage;
        this.Subject = subject;
        this.RoleName = roleName;
        this.AccessScope = accessScope;
        this.ExpiresAtUtc = expiresAtUtc?.ToUniversalTime();
        this.CorrelationId = correlationId;
    }

    public AccessRoleAssignmentLifecycleStage Stage { get; }
    public AccessSubject Subject { get; }
    public string RoleName { get; }
    public AccessScope AccessScope { get; }
    public DateTimeOffset? ExpiresAtUtc { get; }
    public Guid CorrelationId { get; }
}
