namespace Gma.Modules.AccessControl.Application.Queries;

using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Contracts;

public sealed record ListRoleAssignmentsQuery(
    string RoleName,
    int Page,
    int PageSize,
    bool IncludeInactive = false)
    : IQuery<AccessControlPage<AccessControlRoleAssignmentDetails>>;
