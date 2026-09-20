namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public interface IAccessControlSubjectAuthoritySnapshotReader
{
    public const int MaximumExpectedTargetScopeCount = 128;

    Task<AccessControlSubjectAuthoritySnapshot> ReadAsync(
        AccessControlSubjectAuthoritySnapshotRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AccessControlSubjectAuthoritySnapshotRequest(
    AccessSubjectKind SubjectKind,
    string SubjectId,
    AccessScope RootScope,
    IReadOnlyCollection<AccessScope> ExpectedTargetScopes);

public sealed record AccessControlSubjectAuthoritySnapshot(
    AccessControlAuthoritySnapshotStatus Status,
    int ContractVersion,
    AccessSubjectKind SubjectKind,
    string? SubjectId,
    AccessScope? RootScope,
    IReadOnlyList<AccessScope> ExpectedTargetScopes,
    long? ManagementRevision,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? EarliestExpiryAtUtc,
    string? FactsSha256,
    IReadOnlyList<AccessControlAuthorityRoleGrant> RoleGrants,
    IReadOnlyList<AccessControlAuthorityProfileGrant> ProfileGrants);

public sealed record AccessControlAuthorityRoleGrant(
    Guid AssignmentId,
    Guid RoleId,
    string RoleName,
    IReadOnlyList<string> Permissions,
    AccessScope Scope,
    DateTimeOffset? ExpiresAtUtc);

public sealed record AccessControlAuthorityProfileGrant(
    Guid AssignmentId,
    AccessScope AssignmentScope,
    Guid ProfileId,
    AccessScope OwnerScope,
    string ProfileKey,
    AccessProfileStatus ProfileStatus,
    long ProfileVersion,
    IReadOnlyList<string> Permissions);

public enum AccessControlAuthoritySnapshotStatus
{
    Unknown = 0,
    Stable = 1,
    Stale = 2,
    Closed = 3,
    Invalid = 4,
    Unavailable = 5
}
