namespace Gma.Modules.AccessControl.Application.Ports;

using Gma.Framework.AccessControl;

public interface IAccessControlScopeWriteAdmissionReader
{
    Task<bool> AreOpenAsync(
        IReadOnlyCollection<AccessScope> accessScopes,
        CancellationToken cancellationToken);
}
