namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record AssignAccessProfileCommand(
    Guid ProfileId,
    AccessScope OwnerScope,
    AccessScope AssignmentScope,
    AccessSubject Subject,
    AccessSubject Actor) : ITransactionalCommand<AccessProfileAssignmentDetails>
{
    public AssignAccessProfileCommand(
        Guid profileId,
        AccessScope ownerScope,
        AccessSubject subject,
        AccessSubject actor)
        : this(profileId, ownerScope, ownerScope, subject, actor) { }
}
