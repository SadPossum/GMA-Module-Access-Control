namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Contracts;

internal sealed class AccessRoleAssignmentPolicy(IEnumerable<IAccessRoleAssignmentPolicy> policies)
{
    public async Task<bool> IsAllowedAsync(
        AccessSubject subject,
        string roleName,
        AccessScope accessScope,
        DateTimeOffset? expiresAtUtc,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        AccessRoleAssignmentPolicyContext context = new(
            subject,
            roleName,
            accessScope,
            expiresAtUtc,
            permissions);
        foreach (IAccessRoleAssignmentPolicy policy in policies)
        {
            if (!await policy.IsAllowedAsync(context, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }
}
