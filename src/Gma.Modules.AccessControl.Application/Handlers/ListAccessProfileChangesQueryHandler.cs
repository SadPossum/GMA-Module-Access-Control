namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Pagination;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Application.Queries;
using Gma.Modules.AccessControl.Contracts;

internal sealed class ListAccessProfileChangesQueryHandler(IAccessProfileRepository repository)
    : IQueryHandler<ListAccessProfileChangesQuery, AccessControlPage<AccessProfileChangeDetails>>
{
    public async Task<Result<AccessControlPage<AccessProfileChangeDetails>>> HandleAsync(
        ListAccessProfileChangesQuery query,
        CancellationToken cancellationToken)
    {
        if (await repository.GetDetailsAsync(query.ProfileId, query.OwnerScope, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<AccessControlPage<AccessProfileChangeDetails>>(AccessControlApplicationErrors.ProfileNotFound);
        }

        return Result.Success(await repository.ListChangesAsync(
            query.ProfileId, query.OwnerScope, PageRequest.Normalize(query.Page, query.PageSize), cancellationToken)
            .ConfigureAwait(false));
    }
}
