namespace Gma.Modules.AccessControl.Application.Validation;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Application.Commands;

internal sealed class AssignRoleCommandValidator : ICommandValidator<AssignRoleCommand>
{
    public IEnumerable<string> Validate(AssignRoleCommand command)
    {
        if (!AccessSubject.TryCreate(AccessSubjectKind.AdminActor, command.ActorId, out _))
        {
            yield return AccessControlApplicationErrors.SubjectInvalid.Message;
        }

        if (!AccessControlRoleName.TryNormalize(command.RoleName, out _))
        {
            yield return AccessControlApplicationErrors.RoleNameInvalid.Message;
        }

        _ = command.AccessScope;
    }
}
