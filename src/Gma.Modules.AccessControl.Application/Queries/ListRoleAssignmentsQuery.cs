namespace Gma.Modules.AccessControl.Application.Queries;

using Gma.Framework.Cqrs;

public sealed record ListRoleAssignmentsQuery(string RoleName)
    : IQuery<IReadOnlyList<AccessControlRoleAssignmentDetails>>;
