namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record UpdateAccessProfileCommand(
    Guid ProfileId,
    AccessScope OwnerScope,
    string DisplayName,
    string? Description,
    IReadOnlyCollection<string> Permissions,
    long ExpectedVersion,
    AccessSubject Actor) : ITransactionalCommand<AccessProfileDetails>;
