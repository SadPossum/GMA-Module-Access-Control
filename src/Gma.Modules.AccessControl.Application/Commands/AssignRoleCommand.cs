namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record AssignRoleCommand(
    AccessSubjectKind SubjectKind,
    string SubjectId,
    string RoleName,
    AccessScope? AccessScope) : ITransactionalCommand<Unit>
{
    public AssignRoleCommand(string actorId, string roleName, AccessScope? accessScope)
        : this(AccessSubjectKind.AdminActor, actorId, roleName, accessScope)
    {
    }
}
