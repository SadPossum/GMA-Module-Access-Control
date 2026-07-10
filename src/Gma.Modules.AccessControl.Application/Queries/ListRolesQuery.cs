namespace Gma.Modules.AccessControl.Application.Queries;

using Gma.Framework.Cqrs;

public sealed record ListRolesQuery : IQuery<IReadOnlyList<AccessControlRoleDetails>>;
