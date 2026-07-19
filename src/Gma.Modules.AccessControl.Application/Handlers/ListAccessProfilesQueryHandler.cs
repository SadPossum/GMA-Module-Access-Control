namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Pagination;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Application.Queries;
using Gma.Modules.AccessControl.Contracts;

internal sealed class ListAccessProfilesQueryHandler(IAccessProfileRepository repository)
    : IQueryHandler<ListAccessProfilesQuery, AccessControlPage<AccessProfileDetails>>
{
    public async Task<Result<AccessControlPage<AccessProfileDetails>>> HandleAsync(
        ListAccessProfilesQuery query,
        CancellationToken cancellationToken) =>
        Result.Success(await repository.ListAsync(
            query.OwnerScope, query.IncludeArchived, PageRequest.Normalize(query.Page, query.PageSize), cancellationToken)
            .ConfigureAwait(false));
}
