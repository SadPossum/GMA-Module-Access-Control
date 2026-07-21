namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record ReconcileAccessProfileAssignmentsCommand(
    AccessSubject Subject,
    AccessScope OwnerScope,
    IReadOnlyCollection<Guid> ProfileIds,
    AccessSubject Actor) : ITransactionalCommand<AccessProfileAssignmentReconciliationDetails>;
