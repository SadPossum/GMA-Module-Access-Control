namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Queries;

internal sealed class ListAllowedAccessProfilePermissionsQueryHandler(AccessProfilePermissionPolicy policy)
    : IQueryHandler<ListAllowedAccessProfilePermissionsQuery, IReadOnlyList<string>>
{
    public Task<Result<IReadOnlyList<string>>> HandleAsync(
        ListAllowedAccessProfilePermissionsQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(policy.ListAllowed()));
}
