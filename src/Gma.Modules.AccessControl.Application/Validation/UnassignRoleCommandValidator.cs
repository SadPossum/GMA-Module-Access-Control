namespace Gma.Modules.AccessControl.Application.Validation;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Application.Commands;

internal sealed class UnassignRoleCommandValidator : ICommandValidator<UnassignRoleCommand>
{
    public IEnumerable<string> Validate(UnassignRoleCommand command)
    {
        if (!AccessSubject.TryCreate(command.SubjectKind, command.SubjectId, out _))
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
