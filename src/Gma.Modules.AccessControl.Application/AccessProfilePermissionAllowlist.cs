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

        List<AccessRequirement> requirements = [];
        foreach (string value in permissionCodes)
        {
            if (!PermissionCode.TryCreate(value, out PermissionCode? permission) ||
                !this.allowed.Contains(permission.Value))
            {
                return Result.Failure(AccessControlApplicationErrors.ProfilePermissionNotAllowed);
            }

            if (!requirements.Any(requirement => requirement.Permission == permission))
            {
                requirements.Add(new AccessRequirement(actor, permission, scope));
            }
        }

        IReadOnlyList<AccessDecision> decisions = await authorization
            .AuthorizeManyAsync(requirements, cancellationToken)
            .ConfigureAwait(false);
        if (decisions.Count != requirements.Count || decisions.Any(decision => !decision.IsAllowed))
        {
            return Result.Failure(AccessControlApplicationErrors.ProfilePermissionEscalation);
        }

        return Result.Success();
    }
}
