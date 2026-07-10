namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.Cqrs;

public sealed record CreateRoleCommand(string Name) : ITransactionalCommand<AccessControlRoleDetails>;
