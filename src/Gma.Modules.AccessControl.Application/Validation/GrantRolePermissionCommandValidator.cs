namespace Gma.Modules.AccessControl.Application.Validation;

using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Application.Commands;

internal sealed class GrantRolePermissionCommandValidator : ICommandValidator<GrantRolePermissionCommand>
{
    public IEnumerable<string> Validate(GrantRolePermissionCommand command)
    {
        if (!AccessControlRoleName.TryNormalize(command.RoleName, out _))
        {
            yield return AccessControlApplicationErrors.RoleNameInvalid.Message;
        }

        if (!AccessControlPermissionGrant.TryNormalize(command.PermissionCode, out _))
        {
            yield return AccessControlApplicationErrors.PermissionCodeInvalid.Message;
        }
    }
}
