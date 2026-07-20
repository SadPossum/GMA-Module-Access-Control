namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

internal sealed record RevokeAccessProfileAssignmentsCommand(
    AccessSubject Subject,
    AccessScope OwnerScope,
    AccessSubject Actor) : ITransactionalCommand<int>;
