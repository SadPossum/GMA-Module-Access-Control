namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessControlScopeExportPage(
    AccessControlScopeExportStatus Status,
    long ScopeRevision,
    AccessControlScopeExportStore Store,
    IReadOnlyList<AccessControlScopeExportRecord> Records,
    string? NextCursor,
    bool HasMore);
