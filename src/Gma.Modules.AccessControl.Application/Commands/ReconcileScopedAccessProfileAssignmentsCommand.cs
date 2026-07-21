namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Contracts;

public sealed record ReconcileScopedAccessProfileAssignmentsCommand(
    AccessSubject Subject,
    AccessScope OwnerScope,
    IReadOnlyCollection<AccessProfileAssignmentTarget> Targets,
    AccessSubject Actor) : ITransactionalCommand<ScopedAccessProfileAssignmentReconciliationDetails>;
