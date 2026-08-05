namespace Gma.Modules.AccessControl.Persistence.Entities;

internal sealed class AccessControlScopeDestroyReceipt
{
    private AccessControlScopeDestroyReceipt() { }

    private AccessControlScopeDestroyReceipt(
        AccessControlScopeDestroyOperation operation,
        DateTimeOffset completedAtUtc)
    {
        this.OperationId = operation.OperationId;
        this.ScopeHash = operation.ScopeHash;
        this.ScopeValue = operation.ScopeValue;
        this.TransportScopeId = operation.TransportScopeId;
        this.RequestSha256 = operation.RequestSha256;
        this.ExpectedRevision = operation.ExpectedRevision;
        this.ResultingRevision = operation.ResultingRevision;
        this.BatchSize = operation.BatchSize;
        this.RemovedRecordCount = operation.RemovedRecordCount;
        this.CompletedBatchCount = operation.CompletedBatchCount;
        this.RemovalProofVersion = operation.ProofVersion;
        this.RemovalProofSha256 = operation.RemovalProofSha256;
        this.StartedAtUtc = operation.StartedAtUtc;
        this.CompletedAtUtc = completedAtUtc;
    }

    public Guid OperationId { get; private set; }
    public string ScopeHash { get; private set; } = string.Empty;
    public string ScopeValue { get; private set; } = string.Empty;
    public string TransportScopeId { get; private set; } = string.Empty;
    public string RequestSha256 { get; private set; } = string.Empty;
    public long ExpectedRevision { get; private set; }
    public long ResultingRevision { get; private set; }
    public int BatchSize { get; private set; }
    public long RemovedRecordCount { get; private set; }
    public int CompletedBatchCount { get; private set; }
    public int RemovalProofVersion { get; private set; }
    public string RemovalProofSha256 { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }

    public static AccessControlScopeDestroyReceipt? TryCreate(
        AccessControlScopeDestroyOperation operation,
        DateTimeOffset completedAtUtc)
    {
        bool progressShapeValid = operation is not null &&
            ((operation.RemovedRecordCount == 0 &&
              operation.CompletedBatchCount == 0) ||
             (operation.RemovedRecordCount > 0 &&
              operation.CompletedBatchCount > 0));
        if (operation is null ||
            !operation.IsComplete ||
            !progressShapeValid ||
            operation.ResultingRevision < 1 ||
            operation.ProofVersion !=
                AccessControlScopeDestroyOperation.RemovalProofVersion ||
            completedAtUtc < operation.UpdatedAtUtc)
        {
            return null;
        }

        return new AccessControlScopeDestroyReceipt(operation, completedAtUtc);
    }

    public bool Matches(Guid operationId, string requestSha256) =>
        this.OperationId == operationId &&
        string.Equals(
            this.RequestSha256,
            requestSha256,
            StringComparison.Ordinal);
}
