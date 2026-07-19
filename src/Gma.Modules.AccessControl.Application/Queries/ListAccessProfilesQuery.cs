namespace Gma.Modules.AccessControl.Application.Queries;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Contracts;

public sealed record ListAccessProfilesQuery(
    AccessScope OwnerScope,
    bool IncludeArchived,
    int Page,
    int PageSize) : IQuery<AccessControlPage<AccessProfileDetails>>;
