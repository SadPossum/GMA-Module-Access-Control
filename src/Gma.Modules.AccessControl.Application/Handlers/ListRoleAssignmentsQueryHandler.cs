namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Pagination;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Application.Queries;
using Gma.Modules.AccessControl.Contracts;

internal sealed class ListRoleAssignmentsQueryHandler(IAccessControlRbacRepository repository)
    : IQueryHandler<ListRoleAssignmentsQuery, AccessControlPage<AccessControlRoleAssignmentDetails>>
{
    public async Task<Result<AccessControlPage<AccessControlRoleAssignmentDetails>>> HandleAsync(
        ListRoleAssignmentsQuery query,
        CancellationToken cancellationToken)
    {
        if (!AccessControlRoleName.TryNormalize(query.RoleName, out string? roleName))
        {
            return Result.Failure<AccessControlPage<AccessControlRoleAssignmentDetails>>(
                AccessControlApplicationErrors.RoleNameInvalid);
        }

        if (!await repository.RoleExistsAsync(roleName, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<AccessControlPage<AccessControlRoleAssignmentDetails>>(
                AccessControlApplicationErrors.RoleNotFound);
        }

        return Result.Success(await repository.ListRoleAssignmentsPageAsync(
            roleName, PageRequest.Normalize(query.Page, query.PageSize), cancellationToken).ConfigureAwait(false));
    }
}
