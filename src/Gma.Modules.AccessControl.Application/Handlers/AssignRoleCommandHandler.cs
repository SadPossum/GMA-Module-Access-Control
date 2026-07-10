namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;

internal sealed class AssignRoleCommandHandler(IAccessControlRbacRepository repository, ISystemClock clock)
    : ICommandHandler<AssignRoleCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AssignRoleCommand command, CancellationToken cancellationToken)
    {
        if (!AccessSubject.TryCreate(command.SubjectKind, command.SubjectId, out AccessSubject? subject))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.SubjectInvalid);
        }

        if (string.IsNullOrWhiteSpace(command.RoleName))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.RoleNameRequired);
        }

        if (!AccessControlRoleName.TryNormalize(command.RoleName, out string? roleName))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.RoleNameInvalid);
        }

        AccessScope scope = command.AccessScope ?? AccessScope.Global;

        if (!await repository.RoleExistsAsync(roleName, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.RoleNotFound);
        }

        if (await repository.AssignmentExistsAsync(subject, roleName, scope, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.AssignmentAlreadyExists);
        }

        DateTimeOffset now = clock.UtcNow;
        await repository.EnsureSubjectAsync(subject, now, cancellationToken).ConfigureAwait(false);
        await repository.AssignRoleAsync(subject, roleName, scope, now, cancellationToken).ConfigureAwait(false);

        return Result.Success(Unit.Value);
    }
}
