namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;

internal sealed class CreateRoleCommandHandler(IAccessControlRbacRepository repository, ISystemClock clock)
    : ICommandHandler<CreateRoleCommand, AccessControlRoleDetails>
{
    public async Task<Result<AccessControlRoleDetails>> HandleAsync(
        CreateRoleCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure<AccessControlRoleDetails>(AccessControlApplicationErrors.RoleNameRequired);
        }

        if (!AccessControlRoleName.TryNormalize(command.Name, out string? roleName))
        {
            return Result.Failure<AccessControlRoleDetails>(AccessControlApplicationErrors.RoleNameInvalid);
        }

        if (await repository.RoleExistsAsync(roleName, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<AccessControlRoleDetails>(AccessControlApplicationErrors.RoleAlreadyExists);
        }

        AccessControlRoleDetails role = await repository
            .CreateRoleAsync(roleName, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(role);
    }
}
