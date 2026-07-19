namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record ArchiveAccessProfileCommand(
    Guid ProfileId,
    AccessScope OwnerScope,
    long ExpectedVersion,
    AccessSubject Actor) : ITransactionalCommand<Unit>;
