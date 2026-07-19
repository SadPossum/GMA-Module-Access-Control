namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record CreateAccessProfileCommand(
    AccessScope OwnerScope,
    string Key,
    string DisplayName,
    string? Description,
    IReadOnlyCollection<string> Permissions,
    AccessSubject Actor) : ITransactionalCommand<AccessProfileDetails>;
