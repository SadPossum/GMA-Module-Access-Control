namespace Gma.Modules.AccessControl.Contracts;

public interface IAccessControlSubjectAuthoritySnapshotReader
{
    public const int MaximumExpectedTargetScopeCount = 128;

    Task<AccessControlSubjectAuthoritySnapshot> ReadAsync(
        AccessControlSubjectAuthoritySnapshotRequest request,
        CancellationToken cancellationToken = default);
}
