namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.Cqrs;

public sealed record GrantRolePermissionCommand(string RoleName, string PermissionCode) : ITransactionalCommand<Unit>;
