namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;
using Microsoft.Extensions.Options;

internal sealed class BootstrapOwnerCommandHandler(
    IAccessControlRbacRepository repository,
    IOptions<AccessControlOptions> options,
    ISystemClock clock)
    : ICommandHandler<BootstrapOwnerCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(BootstrapOwnerCommand command, CancellationToken cancellationToken)
    {
        if (!command.Confirmed)
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.BootstrapNotAllowed);
        }

        if (!AccessSubject.TryCreate(AccessSubjectKind.AdminActor, command.ActorId, out AccessSubject? subject))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.SubjectInvalid);
        }

        if (!AccessControlRoleName.TryNormalize(options.Value.Bootstrap.OwnerRoleName, out string? roleName))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.RoleNameInvalid);
        }

        bool bootstrapped = await repository.TryBootstrapOwnerAsync(
                subject,
                roleName,
                clock.UtcNow,
                options.Value.Bootstrap.AllowWhenAssignmentsExist,
                cancellationToken)
            .ConfigureAwait(false);
        if (!bootstrapped)
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.BootstrapNotAllowed);
        }

        return Result.Success(Unit.Value);
    }
}
