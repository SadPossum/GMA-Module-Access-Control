namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record AssignRoleCommand(string ActorId, string RoleName, AccessScope? AccessScope) : ITransactionalCommand<Unit>;
