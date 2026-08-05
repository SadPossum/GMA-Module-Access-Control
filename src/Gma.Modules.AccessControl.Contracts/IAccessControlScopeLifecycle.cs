namespace Gma.Modules.AccessControl.Contracts;

public interface IAccessControlScopeLifecycle
{
    Task<AccessControlScopeSnapshot> GetSnapshotAsync(
        AccessControlScopeCoordinate coordinate,
        CancellationToken cancellationToken);

    Task<AccessControlScopeExportPage> ExportAsync(
        AccessControlScopeExportRequest request,
        CancellationToken cancellationToken);

    Task<AccessControlScopeDestroyResult> DestroyBatchAsync(
        AccessControlScopeDestroyRequest request,
        CancellationToken cancellationToken);
}
