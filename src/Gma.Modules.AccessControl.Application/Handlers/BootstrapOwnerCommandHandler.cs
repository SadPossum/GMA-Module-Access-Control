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

        bool hasAssignments = await repository.HasAnyAssignmentsAsync(cancellationToken).ConfigureAwait(false);
        if (hasAssignments && !options.Value.Bootstrap.AllowWhenAssignmentsExist)
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.BootstrapNotAllowed);
        }

        if (!AccessControlRoleName.TryNormalize(options.Value.Bootstrap.OwnerRoleName, out string? roleName))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.RoleNameInvalid);
        }

        DateTimeOffset now = clock.UtcNow;
        await repository.EnsureSubjectAsync(subject, now, cancellationToken).ConfigureAwait(false);
        await repository.EnsureRoleAsync(roleName, now, cancellationToken).ConfigureAwait(false);
        await repository.EnsureRolePermissionAsync(roleName, AccessControlPermissionGrant.OwnerWildcard, now, cancellationToken)
            .ConfigureAwait(false);
        await repository.EnsureRoleAssignmentAsync(subject, roleName, AccessScope.Global, now, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(Unit.Value);
    }
}
