namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Contracts;

public sealed record EnsureAccessProfileCommand(
    AccessScope OwnerScope,
    AccessProfileDefinition Definition,
    AccessSubject Actor) : ITransactionalCommand<AccessProfileDetails>;
