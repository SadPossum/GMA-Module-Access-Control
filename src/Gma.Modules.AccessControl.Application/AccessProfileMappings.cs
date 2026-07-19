namespace Gma.Modules.AccessControl.Application;

using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Enums;

internal static class AccessProfileMappings
{
    public static AccessProfileDetails ToDetails(AccessProfile profile, int assignmentCount)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new AccessProfileDetails(
            profile.Id,
            profile.OwnerScope,
            profile.Key,
            profile.DisplayName,
            profile.Description,
            AccessProfileStatusNames.GetName(profile.Status),
            profile.Version,
            profile.Permissions.Select(permission => permission.PermissionCode).Order(StringComparer.Ordinal).ToArray(),
            assignmentCount,
            profile.CreatedAtUtc,
            profile.LastChangedAtUtc);
    }

}
