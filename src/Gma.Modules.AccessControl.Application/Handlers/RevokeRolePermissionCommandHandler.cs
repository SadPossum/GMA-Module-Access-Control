namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;

internal sealed class RevokeRolePermissionCommandHandler(IAccessControlRbacRepository repository)
    : ICommandHandler<RevokeRolePermissionCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        RevokeRolePermissionCommand command,
        CancellationToken cancellationToken)
    {
        if (!AccessControlRoleName.TryNormalize(command.RoleName, out string? roleName))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.RoleNameInvalid);
        }

        if (!await repository.RoleExistsAsync(roleName, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.RoleNotFound);
        }

        if (!AccessControlPermissionGrant.TryNormalize(command.PermissionCode, out string? permission))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.PermissionCodeInvalid);
        }

        AccessControlRemovalOutcome outcome = await repository
            .RevokeRolePermissionAsync(roleName, permission, cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            AccessControlRemovalOutcome.Removed => Result.Success(Unit.Value),
            AccessControlRemovalOutcome.NotFound => Result.Failure<Unit>(AccessControlApplicationErrors.PermissionNotGranted),
            AccessControlRemovalOutcome.LastOwnerProtected => Result.Failure<Unit>(AccessControlApplicationErrors.LastOwnerProtected),
            _ => throw new InvalidOperationException($"Unsupported access-control removal outcome '{outcome}'.")
        };
    }
}
