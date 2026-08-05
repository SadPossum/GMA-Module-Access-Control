namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessControlScopeExportRequest(
    AccessControlScopeCoordinate Coordinate,
    long ExpectedRevision,
    AccessControlScopeExportStore Store,
    string? AfterCursor,
    int PageSize);
