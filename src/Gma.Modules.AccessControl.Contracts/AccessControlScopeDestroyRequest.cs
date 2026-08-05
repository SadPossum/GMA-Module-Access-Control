namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessControlScopeDestroyRequest(
    Guid OperationId,
    AccessControlScopeCoordinate Coordinate,
    long ExpectedRevision,
    int BatchSize);
