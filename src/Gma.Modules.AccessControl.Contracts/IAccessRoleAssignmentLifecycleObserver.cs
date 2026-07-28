namespace Gma.Modules.AccessControl.Contracts;

public interface IAccessRoleAssignmentLifecycleObserver
{
    ValueTask ObserveAsync(
        AccessRoleAssignmentLifecycleEvent lifecycleEvent,
        CancellationToken cancellationToken = default);
}
