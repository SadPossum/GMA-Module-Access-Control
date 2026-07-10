namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Modules.AccessControl.Application.Ports;

internal sealed class PersistedAccessGrantScopeReader(IAccessControlRbacRepository repository)
    : IAccessGrantScopeReader
{
    public Task<IReadOnlyList<AccessGrantScope>> ListGrantedScopesAsync(
        AccessSubject subject,
        PermissionCode permission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(permission);

        return repository.ListGrantedScopesAsync(subject, permission, cancellationToken);
    }
}
