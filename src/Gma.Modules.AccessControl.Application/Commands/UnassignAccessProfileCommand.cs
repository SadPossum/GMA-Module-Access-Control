namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record UnassignAccessProfileCommand(
    Guid ProfileId,
    AccessScope OwnerScope,
    AccessScope AssignmentScope,
    AccessSubject Subject,
    AccessSubject Actor) : ITransactionalCommand<Unit>
{
    public UnassignAccessProfileCommand(
        Guid profileId,
        AccessScope ownerScope,
        AccessSubject subject,
        AccessSubject actor)
        : this(profileId, ownerScope, ownerScope, subject, actor) { }
}
