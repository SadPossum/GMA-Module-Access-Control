namespace Gma.Modules.AccessControl.Api;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Contracts;

internal static class AccessProfileApiMappings
{
    public static AccessProfileDto ToDto(AccessProfileDetails profile) =>
        new(
            profile.Id,
            profile.OwnerScope.Value,
            profile.Key,
            profile.DisplayName,
            profile.Description,
            profile.Status,
            profile.Version,
            profile.Permissions,
            profile.AssignmentCount,
            profile.CreatedAtUtc,
            profile.LastChangedAtUtc);

    public static AccessProfileAssignmentDto ToDto(AccessProfileAssignmentDetails assignment) =>
        new(
            assignment.Id,
            assignment.ProfileId,
            AccessSubjectKindNames.GetName(assignment.SubjectKind),
            assignment.SubjectId,
            AccessSubjectKindNames.GetName(assignment.CreatedByKind),
            assignment.CreatedById,
            assignment.CreatedAtUtc,
            assignment.AssignmentScope.Value);

    public static AccessProfileChangeDto ToDto(AccessProfileChangeDetails change) =>
        new(
            change.Id,
            change.ProfileId,
            change.Kind,
            AccessSubjectKindNames.GetName(change.ActorKind),
            change.ActorId,
            change.SubjectKind.HasValue ? AccessSubjectKindNames.GetName(change.SubjectKind.Value) : null,
            change.SubjectId,
            change.ProfileVersion,
            change.OccurredAtUtc,
            change.AssignmentScope?.Value);

    public static AccessControlPage<TTarget> ToPage<TSource, TTarget>(
        AccessControlPage<TSource> page,
        Func<TSource, TTarget> map) =>
        new(page.Items.Select(map).ToArray(), page.Page, page.PageSize, page.HasMore);
}
