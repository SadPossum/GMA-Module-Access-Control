namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;

internal sealed class GrantRolePermissionCommandHandler(IAccessControlRbacRepository repository, ISystemClock clock)
    : ICommandHandler<GrantRolePermissionCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        GrantRolePermissionCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RoleName))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.RoleNameRequired);
        }

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

        DateTimeOffset now = clock.UtcNow;
        AccessControlRolePermissionGrantPersistenceOutcome outcome = await repository
            .GrantRolePermissionAsync(roleName, permission, now, cancellationToken)
            .ConfigureAwait(false);
        if (outcome == AccessControlRolePermissionGrantPersistenceOutcome.TemporaryAssignmentsExist)
        {
            return Result.Failure<Unit>(
                AccessControlApplicationErrors.RolePermissionExpansionTemporaryAssignmentsExist);
        }

        if (outcome == AccessControlRolePermissionGrantPersistenceOutcome.AlreadyGranted)
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.PermissionAlreadyGranted);
        }

        return Result.Success(Unit.Value);
    }
}
