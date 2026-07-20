namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Contracts;

internal sealed class AccessProfileAssignmentRevoker(IRequestDispatcher dispatcher)
    : IAccessProfileAssignmentRevoker
{
    public async Task<int> RevokeAllAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        AccessSubject actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(ownerScope);
        ArgumentNullException.ThrowIfNull(actor);
        if (ownerScope.IsGlobal)
        {
            throw new ArgumentException("A non-global access-profile owner scope is required.", nameof(ownerScope));
        }

        Gma.Framework.Results.Result<int> result = await dispatcher.SendAsync(
                new RevokeAccessProfileAssignmentsCommand(subject, ownerScope, actor),
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(result.Error.Message);
    }
}
