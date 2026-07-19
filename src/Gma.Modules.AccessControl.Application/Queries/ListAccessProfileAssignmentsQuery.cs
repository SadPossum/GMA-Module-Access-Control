namespace Gma.Modules.AccessControl.Application.Queries;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Contracts;

public sealed record ListAccessProfileAssignmentsQuery(
    Guid ProfileId,
    AccessScope OwnerScope,
    int Page,
    int PageSize) : IQuery<AccessControlPage<AccessProfileAssignmentDetails>>;
