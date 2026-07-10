namespace Gma.Modules.AccessControl.Application.Validation;

using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Application.Commands;

internal sealed class CreateRoleCommandValidator : ICommandValidator<CreateRoleCommand>
{
    public IEnumerable<string> Validate(CreateRoleCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            yield return AccessControlApplicationErrors.RoleNameRequired.Message;
        }
        else if (!AccessControlRoleName.TryNormalize(command.Name, out _))
        {
            yield return AccessControlApplicationErrors.RoleNameInvalid.Message;
        }
    }
}
