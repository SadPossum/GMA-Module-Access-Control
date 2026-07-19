namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Contracts;

internal sealed record AccessProfileAllowedPermission(PermissionCode Permission);

internal sealed class AccessProfilePermissionPolicy(
    IEnumerable<AccessProfileAllowedPermission> registrations,
    IAccessAuthorizationService authorization)
{
    private readonly HashSet<string> allowed = registrations
        .Select(registration => registration.Permission.Value)
        .ToHashSet(StringComparer.Ordinal);

    public IReadOnlyList<string> ListAllowed() => this.allowed.Order(StringComparer.Ordinal).ToArray();

    public async Task<Result> ValidateDelegationAsync(
        AccessSubject actor,
        AccessScope scope,
        IReadOnlyCollection<string> permissionCodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(permissionCodes);

        foreach (string value in permissionCodes.Distinct(StringComparer.Ordinal))
        {
            if (!PermissionCode.TryCreate(value, out PermissionCode? permission) ||
                !this.allowed.Contains(permission.Value))
            {
                return Result.Failure(AccessControlApplicationErrors.ProfilePermissionNotAllowed);
            }

            AccessDecision decision = await authorization
                .AuthorizeAsync(new AccessRequirement(actor, permission, scope), cancellationToken)
                .ConfigureAwait(false);
            if (!decision.IsAllowed)
            {
                return Result.Failure(AccessControlApplicationErrors.ProfilePermissionEscalation);
            }
        }

        return Result.Success();
    }
}
