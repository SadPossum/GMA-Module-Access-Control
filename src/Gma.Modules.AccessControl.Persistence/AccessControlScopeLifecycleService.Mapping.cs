namespace Gma.Modules.AccessControl.Persistence;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Persistence.Entities;
using ContractChangeKind = Gma.Modules.AccessControl.Contracts.AccessProfileChangeKind;
using ContractStatus = Gma.Modules.AccessControl.Contracts.AccessProfileStatus;

internal sealed partial class AccessControlScopeLifecycleService
{
    private static AccessControlRoleAssignmentExportRecord Map(
        AccessSubjectRoleAssignment assignment) =>
        new(
            assignment.Id,
            (AccessSubjectKind)assignment.SubjectKind,
            assignment.SubjectId,
            assignment.Role!.Name,
            assignment.Role.Permissions
                .Select(permission => permission.PermissionCode)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            assignment.ScopeValue,
            assignment.CreatedAtUtc,
            assignment.ExpiresAtUtc,
            assignment.RevokedAtUtc);

    private static AccessControlProfileExportRecord Map(
        AccessProfile profile) =>
        new(
            profile.Id,
            profile.OwnerScopeValue,
            profile.Key,
            profile.DisplayName,
            profile.Description,
            (ContractStatus)(int)profile.Status,
            profile.Version,
            (AccessSubjectKind)profile.CreatedByKind,
            profile.CreatedById,
            profile.CreatedAtUtc,
            (AccessSubjectKind)profile.LastChangedByKind,
            profile.LastChangedById,
            profile.LastChangedAtUtc,
            profile.Permissions
                .Select(permission => permission.PermissionCode)
                .Order(StringComparer.Ordinal)
                .ToArray());

    private static AccessControlProfileAssignmentExportRecord Map(
        AccessProfileAssignment assignment) =>
        new(
            assignment.Id,
            assignment.ProfileId,
            assignment.Profile!.OwnerScopeValue,
            assignment.Profile.Key,
            assignment.AssignmentScopeValue,
            (AccessSubjectKind)assignment.SubjectKind,
            assignment.SubjectId,
            (AccessSubjectKind)assignment.CreatedByKind,
            assignment.CreatedById,
            assignment.CreatedAtUtc);

    private static AccessControlProfileChangeExportRecord Map(
        AccessProfileChange change) =>
        new(
            change.Id,
            change.ProfileId,
            change.Profile!.OwnerScopeValue,
            change.Profile.Key,
            (ContractChangeKind)(int)change.Kind,
            (AccessSubjectKind)change.ActorKind,
            change.ActorId,
            change.SubjectKind is null
                ? null
                : (AccessSubjectKind)change.SubjectKind.Value,
            change.SubjectId,
            change.AssignmentScopeValue,
            change.ProfileVersion,
            change.OccurredAtUtc);
}
