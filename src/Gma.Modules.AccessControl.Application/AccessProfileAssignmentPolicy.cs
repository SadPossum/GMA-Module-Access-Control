namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;

internal sealed class AccessProfileAssignmentPolicy(IEnumerable<IAccessProfileAssignmentPolicy> policies)
{
    public Task<bool> IsAllowedAsync(
        AccessProfile profile,
        AccessScope ownerScope,
        AccessSubject actor,
        AccessSubject subject,
        CancellationToken cancellationToken) =>
        this.IsAllowedAsync(profile, ownerScope, ownerScope, actor, subject, cancellationToken);

    public async Task<bool> IsAllowedAsync(
        AccessProfile profile,
        AccessScope ownerScope,
        AccessScope assignmentScope,
        AccessSubject actor,
        AccessSubject subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        AccessProfileAssignmentPolicyContext context = new(
            profile.Id,
            profile.Key,
            ownerScope,
            actor,
            subject,
            profile.Permissions.Select(permission => permission.PermissionCode).ToArray(),
            assignmentScope);
        foreach (IAccessProfileAssignmentPolicy policy in policies)
        {
            if (!await policy.IsAllowedAsync(context, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }
}
