namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessControlScopeDestroyReceipt(
    Guid OperationId,
    long ResultingRevision,
    int BatchSize,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
