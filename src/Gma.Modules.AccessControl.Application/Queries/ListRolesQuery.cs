namespace Gma.Modules.AccessControl.Application.Queries;

using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Contracts;

public sealed record ListRolesQuery(int Page, int PageSize)
    : IQuery<AccessControlPage<AccessControlRoleDetails>>;
