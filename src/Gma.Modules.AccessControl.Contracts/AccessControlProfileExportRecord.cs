namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessControlProfileExportRecord(
    Guid ProfileId,
    string OwnerScopeValue,
    string Key,
    string DisplayName,
    string Description,
    AccessProfileStatus Status,
    long Version,
    AccessSubjectKind CreatedByKind,
    string CreatedById,
    DateTimeOffset CreatedAtUtc,
    AccessSubjectKind LastChangedByKind,
    string LastChangedById,
    DateTimeOffset LastChangedAtUtc,
    IReadOnlyList<string> Permissions)
    : AccessControlScopeExportRecord;
