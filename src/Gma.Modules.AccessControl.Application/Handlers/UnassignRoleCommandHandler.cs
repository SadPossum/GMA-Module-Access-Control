namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;

internal sealed class UnassignRoleCommandHandler(IAccessControlRbacRepository repository)
    : ICommandHandler<UnassignRoleCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UnassignRoleCommand command, CancellationToken cancellationToken)
    {
        if (!AccessSubject.TryCreate(command.SubjectKind, command.SubjectId, out AccessSubject? subject))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.SubjectInvalid);
        }

        if (!AccessControlRoleName.TryNormalize(command.RoleName, out string? roleName))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.RoleNameInvalid);
        }

        if (!await repository.RoleExistsAsync(roleName, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.RoleNotFound);
        }

        AccessControlRemovalOutcome outcome = await repository.UnassignRoleAsync(
                subject,
                roleName,
                command.AccessScope ?? AccessScope.Global,
                cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            AccessControlRemovalOutcome.Removed => Result.Success(Unit.Value),
            AccessControlRemovalOutcome.NotFound => Result.Failure<Unit>(AccessControlApplicationErrors.AssignmentNotFound),
            AccessControlRemovalOutcome.LastOwnerProtected => Result.Failure<Unit>(AccessControlApplicationErrors.LastOwnerProtected),
            _ => throw new InvalidOperationException($"Unsupported access-control removal outcome '{outcome}'.")
        };
    }
}
