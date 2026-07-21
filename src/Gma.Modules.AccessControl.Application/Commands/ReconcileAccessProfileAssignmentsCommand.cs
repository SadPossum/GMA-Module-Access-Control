namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record ReconcileAccessProfileAssignmentsCommand(
    AccessSubject Subject,
    AccessScope OwnerScope,
    AccessScope AssignmentScope,
    IReadOnlyCollection<Guid> ProfileIds,
    AccessSubject Actor) : ITransactionalCommand<AccessProfileAssignmentReconciliationDetails>
{
    public ReconcileAccessProfileAssignmentsCommand(
        AccessSubject subject,
        AccessScope ownerScope,
        IReadOnlyCollection<Guid> profileIds,
        AccessSubject actor)
        : this(subject, ownerScope, ownerScope, profileIds, actor) { }
}
