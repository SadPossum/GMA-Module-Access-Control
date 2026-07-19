namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Pagination;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Application.Queries;
using Gma.Modules.AccessControl.Contracts;

internal sealed class ListRolesQueryHandler(IAccessControlRbacRepository repository)
    : IQueryHandler<ListRolesQuery, AccessControlPage<AccessControlRoleDetails>>
{
    public async Task<Result<AccessControlPage<AccessControlRoleDetails>>> HandleAsync(
        ListRolesQuery query,
        CancellationToken cancellationToken) =>
        Result.Success(await repository.ListRolesPageAsync(
            PageRequest.Normalize(query.Page, query.PageSize), cancellationToken).ConfigureAwait(false));
}
