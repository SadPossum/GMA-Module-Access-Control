namespace Gma.Modules.AccessControl.Persistence.Entities;

internal sealed class AccessControlScopeDestroyOperation
{
    public const int RemovalProofVersion = 1;
    public static readonly string InitialRemovalProofSha256 =
        AccessControlLifecycleHashes.Sha256(
            "gma-access-control-scope-destroy-proof/v1|empty");

    private AccessControlScopeDestroyOperation() { }

    private AccessControlScopeDestroyOperation(
        Guid operationId,
        string scopeHash,
        string scopeValue,
        string transportScopeId,
        string requestSha256,
        long expectedRevision,
        long resultingRevision,
        int batchSize,
        DateTimeOffset startedAtUtc)
    {
        this.OperationId = operationId;
        this.ScopeHash = scopeHash;
        this.ScopeValue = scopeValue;
        this.TransportScopeId = transportScopeId;
        this.RequestSha256 = requestSha256;
        this.ExpectedRevision = expectedRevision;
        this.ResultingRevision = resultingRevision;
        this.BatchSize = batchSize;
        this.Stage = AccessControlScopeDestroyStage.InboxMessages;
        this.RemovalProofSha256 = InitialRemovalProofSha256;
        this.StartedAtUtc = startedAtUtc;
        this.UpdatedAtUtc = startedAtUtc;
    }

    public Guid OperationId { get; private set; }
    public string ScopeHash { get; private set; } = string.Empty;
    public string ScopeValue { get; private set; } = string.Empty;
    public string TransportScopeId { get; private set; } = string.Empty;
    public string RequestSha256 { get; private set; } = string.Empty;
    public long ExpectedRevision { get; private set; }
    public long ResultingRevision { get; private set; }
    public int BatchSize { get; private set; }
    public AccessControlScopeDestroyStage Stage { get; private set; }
    public long RemovedRecordCount { get; private set; }
    public int CompletedBatchCount { get; private set; }
    public int ProofVersion { get; private set; } = RemovalProofVersion;
    public string RemovalProofSha256 { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public bool IsComplete => this.Stage == AccessControlScopeDestroyStage.Completed;

    public static AccessControlScopeDestroyOperation? TryCreate(
        Guid operationId,
        string scopeValue,
        string transportScopeId,
        string requestSha256,
        long expectedRevision,
        long resultingRevision,
        int batchSize,
        int maximumBatchSize,
        DateTimeOffset startedAtUtc)
    {
        if (operationId == Guid.Empty ||
            !AccessControlLifecycleHashes.IsSha256(requestSha256) ||
            expectedRevision < 0 ||
            expectedRevision == long.MaxValue ||
            resultingRevision <= expectedRevision ||
            batchSize is < 1 ||
            batchSize > maximumBatchSize ||
            startedAtUtc == default)
        {
            return null;
        }

        return new AccessControlScopeDestroyOperation(
            operationId,
            AccessScopeIndex.Create(scopeValue),
            scopeValue,
            transportScopeId,
            requestSha256,
            expectedRevision,
            resultingRevision,
            batchSize,
            startedAtUtc);
    }

    public bool Matches(Guid operationId, string requestSha256) =>
        this.OperationId == operationId &&
        string.Equals(
            this.RequestSha256,
            requestSha256,
            StringComparison.Ordinal);

    public bool RecordBatch(
        AccessControlScopeDestroyStage stage,
        int removedRecordCount,
        string removedRecordIdsSha256,
        bool stageCompleted,
        DateTimeOffset recordedAtUtc)
    {
        if (this.IsComplete ||
            stage != this.Stage ||
            removedRecordCount is < 1 ||
            removedRecordCount > this.BatchSize ||
            !AccessControlLifecycleHashes.IsSha256(removedRecordIdsSha256) ||
            recordedAtUtc < this.UpdatedAtUtc ||
            this.RemovedRecordCount > long.MaxValue - removedRecordCount ||
            this.CompletedBatchCount == int.MaxValue)
        {
            return false;
        }

        int nextBatch = this.CompletedBatchCount + 1;
        this.RemovalProofSha256 = AccessControlLifecycleHashes.Sha256(
            "gma-access-control-scope-destroy-proof/v1|" +
            $"{this.RemovalProofSha256}|{nextBatch}|{(int)stage}|" +
            $"{removedRecordCount}|{removedRecordIdsSha256}");
        this.RemovedRecordCount += removedRecordCount;
        this.CompletedBatchCount = nextBatch;
        this.UpdatedAtUtc = recordedAtUtc;
        if (stageCompleted)
        {
            this.Stage = Next(stage);
        }

        return true;
    }

    public bool AdvanceEmptyStage(DateTimeOffset recordedAtUtc)
    {
        if (this.IsComplete || recordedAtUtc < this.UpdatedAtUtc)
        {
            return false;
        }

        this.Stage = Next(this.Stage);
        this.UpdatedAtUtc = recordedAtUtc;
        return true;
    }

    private static AccessControlScopeDestroyStage Next(
        AccessControlScopeDestroyStage stage) =>
        stage switch
        {
            AccessControlScopeDestroyStage.InboxMessages =>
                AccessControlScopeDestroyStage.ProfileChanges,
            AccessControlScopeDestroyStage.ProfileChanges =>
                AccessControlScopeDestroyStage.ProfileAssignments,
            AccessControlScopeDestroyStage.ProfileAssignments =>
                AccessControlScopeDestroyStage.RoleAssignments,
            AccessControlScopeDestroyStage.RoleAssignments =>
                AccessControlScopeDestroyStage.Profiles,
            AccessControlScopeDestroyStage.Profiles =>
                AccessControlScopeDestroyStage.OrphanPrincipals,
            AccessControlScopeDestroyStage.OrphanPrincipals =>
                AccessControlScopeDestroyStage.Completed,
            _ => throw new InvalidOperationException(
                "The access-control scope destruction stage is invalid.")
        };
}

internal enum AccessControlScopeDestroyStage
{
    Unknown = 0,
    InboxMessages = 1,
    ProfileChanges = 2,
    ProfileAssignments = 3,
    RoleAssignments = 4,
    Profiles = 5,
    OrphanPrincipals = 6,
    Completed = 7
}
