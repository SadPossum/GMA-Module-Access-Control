namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record AssignAccessProfileCommand(
    Guid ProfileId,
    AccessScope OwnerScope,
    AccessSubject Subject,
    AccessSubject Actor) : ITransactionalCommand<AccessProfileAssignmentDetails>;
