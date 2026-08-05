namespace Gma.Modules.AccessControl.Persistence.Entities;

internal sealed class AccessControlScopeState
{
    private AccessControlScopeState() { }

    private AccessControlScopeState(
        string scopeHash,
        string scopeValue,
        string transportScopeId)
    {
        this.ScopeHash = scopeHash;
        this.ScopeValue = scopeValue;
        this.TransportScopeId = transportScopeId;
    }

    public string ScopeHash { get; private set; } = string.Empty;
    public string ScopeValue { get; private set; } = string.Empty;
    public string TransportScopeId { get; private set; } = string.Empty;
    public bool IsClosed { get; private set; }
    public long CloseRevision { get; private set; }
    public Guid? CloseOperationId { get; private set; }
    public string? CloseRequestSha256 { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public static AccessControlScopeState Create(
        string scopeValue,
        string transportScopeId) =>
        new(
            AccessScopeIndex.Create(scopeValue),
            scopeValue,
            transportScopeId);

    public AccessControlScopeCloseTransition Close(
        Guid operationId,
        string requestSha256,
        long closeRevision,
        DateTimeOffset closedAtUtc)
    {
        if (operationId == Guid.Empty ||
            !AccessControlLifecycleHashes.IsSha256(requestSha256) ||
            closeRevision < 1 ||
            closedAtUtc == default)
        {
            return AccessControlScopeCloseTransition.Invalid;
        }

        if (this.IsClosed)
        {
            return this.CloseOperationId == operationId &&
                string.Equals(
                    this.CloseRequestSha256,
                    requestSha256,
                    StringComparison.Ordinal)
                ? AccessControlScopeCloseTransition.Replayed
                : AccessControlScopeCloseTransition.Conflict;
        }

        this.IsClosed = true;
        this.CloseRevision = closeRevision;
        this.CloseOperationId = operationId;
        this.CloseRequestSha256 = requestSha256;
        this.ClosedAtUtc = closedAtUtc;
        return AccessControlScopeCloseTransition.Completed;
    }
}

internal enum AccessControlScopeCloseTransition
{
    Invalid = 0,
    Completed = 1,
    Replayed = 2,
    Conflict = 3
}
