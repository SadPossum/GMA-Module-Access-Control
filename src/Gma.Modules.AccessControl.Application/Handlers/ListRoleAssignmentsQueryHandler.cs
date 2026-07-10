namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Application.Queries;

internal sealed class ListRoleAssignmentsQueryHandler(IAccessControlRbacRepository repository)
    : IQueryHandler<ListRoleAssignmentsQuery, IReadOnlyList<AccessControlRoleAssignmentDetails>>
{
    public async Task<Result<IReadOnlyList<AccessControlRoleAssignmentDetails>>> HandleAsync(
        ListRoleAssignmentsQuery query,
        CancellationToken cancellationToken)
    {
        if (!AccessControlRoleName.TryNormalize(query.RoleName, out string? roleName))
        {
            return Result.Failure<IReadOnlyList<AccessControlRoleAssignmentDetails>>(
                AccessControlApplicationErrors.RoleNameInvalid);
        }

        if (!await repository.RoleExistsAsync(roleName, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<IReadOnlyList<AccessControlRoleAssignmentDetails>>(
                AccessControlApplicationErrors.RoleNotFound);
        }

        return Result.Success(await repository.ListRoleAssignmentsAsync(roleName, cancellationToken).ConfigureAwait(false));
    }
}
