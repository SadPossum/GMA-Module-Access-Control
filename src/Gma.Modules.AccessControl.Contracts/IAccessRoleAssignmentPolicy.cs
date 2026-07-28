namespace Gma.Modules.AccessControl.Contracts;

public interface IAccessRoleAssignmentPolicy
{
    ValueTask<bool> IsAllowedAsync(
        AccessRoleAssignmentPolicyContext context,
        CancellationToken cancellationToken = default);
}
