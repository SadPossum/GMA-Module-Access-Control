namespace Gma.Modules.AccessControl.Persistence;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Application.Ports;

internal sealed class AccessControlScopeAdmissionPolicy(
    AccessControlDbContext dbContext)
    : IAccessControlScopeWriteAdmissionReader
{
    public Task<bool> AreOpenAsync(
        IReadOnlyCollection<AccessScope> accessScopes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessScopes);
        if (accessScopes.Any(scope => scope is null))
        {
            throw new ArgumentException(
                "Access scopes cannot contain null values.",
                nameof(accessScopes));
        }

        return dbContext.AreScopesOpenAsync(
            accessScopes.Select(scope => scope.Value),
            cancellationToken);
    }
}
