namespace Gma.Modules.AccessControl.Contracts;

public interface IAccessProfileAssignmentPolicy
{
    ValueTask<bool> IsAllowedAsync(
        AccessProfileAssignmentPolicyContext context,
        CancellationToken cancellationToken = default);
}
