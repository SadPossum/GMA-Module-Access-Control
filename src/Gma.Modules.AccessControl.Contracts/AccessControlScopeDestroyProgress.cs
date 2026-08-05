namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessControlScopeDestroyProgress(
    Guid OperationId,
    long ResultingRevision,
    int BatchSize,
    AccessControlScopeDestructionStage Stage,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc);
