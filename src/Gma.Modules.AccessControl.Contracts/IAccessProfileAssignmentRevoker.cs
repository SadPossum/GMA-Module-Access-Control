namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public interface IAccessProfileAssignmentRevoker
{
    Task<int> RevokeAllAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        AccessSubject actor,
        CancellationToken cancellationToken = default);
}
