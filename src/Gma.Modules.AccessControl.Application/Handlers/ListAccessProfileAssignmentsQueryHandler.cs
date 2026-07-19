namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Pagination;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Application.Queries;
using Gma.Modules.AccessControl.Contracts;

internal sealed class ListAccessProfileAssignmentsQueryHandler(IAccessProfileRepository repository)
    : IQueryHandler<ListAccessProfileAssignmentsQuery, AccessControlPage<AccessProfileAssignmentDetails>>
{
    public async Task<Result<AccessControlPage<AccessProfileAssignmentDetails>>> HandleAsync(
        ListAccessProfileAssignmentsQuery query,
        CancellationToken cancellationToken)
    {
        if (await repository.GetDetailsAsync(query.ProfileId, query.OwnerScope, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<AccessControlPage<AccessProfileAssignmentDetails>>(AccessControlApplicationErrors.ProfileNotFound);
        }

        return Result.Success(await repository.ListAssignmentsAsync(
            query.ProfileId, query.OwnerScope, PageRequest.Normalize(query.Page, query.PageSize), cancellationToken)
            .ConfigureAwait(false));
    }
}
