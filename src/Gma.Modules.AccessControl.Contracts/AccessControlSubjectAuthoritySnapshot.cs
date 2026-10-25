namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

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
