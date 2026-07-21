namespace Gma.Modules.AccessControl.Application;

using Gma.Modules.AccessControl.Contracts;

internal static class AccessProfileContractMappings
{
    public static AccessProfileDto ToContract(AccessProfileDetails profile) =>
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

    public static AccessControlPage<AccessProfileDto> ToContract(
        AccessControlPage<AccessProfileDetails> page) =>
        new(
            page.Items.Select(ToContract).ToArray(),
            page.Page,
            page.PageSize,
            page.HasMore);
}
