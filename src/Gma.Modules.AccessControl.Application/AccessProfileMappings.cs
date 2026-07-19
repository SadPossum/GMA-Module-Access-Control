namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using DomainStatus = Gma.Modules.AccessControl.Domain.Enums.AccessProfileStatus;

internal static class AccessProfileMappings
{
    public static AccessProfileDetails ToDetails(AccessProfile profile, int assignmentCount)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new AccessProfileDetails(
            profile.Id,
            AccessScope.Parse(profile.OwnerScopeValue),
            profile.Key,
            profile.DisplayName,
            profile.Description,
            ToContract(profile.Status),
            profile.Version,
            profile.Permissions.Select(permission => permission.PermissionCode).Order(StringComparer.Ordinal).ToArray(),
            assignmentCount,
            profile.CreatedAtUtc,
            profile.LastChangedAtUtc);
    }

    private static AccessProfileStatus ToContract(DomainStatus status) =>
        status switch
        {
            DomainStatus.Active => AccessProfileStatus.Active,
            DomainStatus.Archived => AccessProfileStatus.Archived,
            _ => throw new InvalidOperationException($"Access-profile status '{status}' is invalid.")
        };
}
