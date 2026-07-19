namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Application.Queries;

internal sealed class GetAccessProfileQueryHandler(IAccessProfileRepository repository)
    : IQueryHandler<GetAccessProfileQuery, AccessProfileDetails>
{
    public async Task<Result<AccessProfileDetails>> HandleAsync(
        GetAccessProfileQuery query,
        CancellationToken cancellationToken)
    {
        AccessProfileDetails? profile = await repository
            .GetDetailsAsync(query.ProfileId, query.OwnerScope, cancellationToken).ConfigureAwait(false);
        return profile is null
            ? Result.Failure<AccessProfileDetails>(AccessControlApplicationErrors.ProfileNotFound)
            : Result.Success(profile);
    }
}
