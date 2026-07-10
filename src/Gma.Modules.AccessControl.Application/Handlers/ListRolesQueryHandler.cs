namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Application.Queries;

internal sealed class ListRolesQueryHandler(IAccessControlRbacRepository repository)
    : IQueryHandler<ListRolesQuery, IReadOnlyList<AccessControlRoleDetails>>
{
    public async Task<Result<IReadOnlyList<AccessControlRoleDetails>>> HandleAsync(
        ListRolesQuery query,
        CancellationToken cancellationToken) =>
        Result.Success(await repository.ListRolesAsync(cancellationToken).ConfigureAwait(false));
}
