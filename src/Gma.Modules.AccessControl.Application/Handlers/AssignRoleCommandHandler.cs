namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;

internal sealed class AssignRoleCommandHandler(
    IAccessControlRbacRepository repository,
    AccessControlScopeWriteAdmission scopeWriteAdmission,
    AccessRoleAssignmentPolicy assignmentPolicy,
    ISystemClock clock)
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
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset? expiresAtUtc = command.ExpiresAtUtc is null
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(
                command.ExpiresAtUtc.Value.ToUniversalTime().ToUnixTimeMilliseconds());
        if (expiresAtUtc is not null &&
            expiresAtUtc.Value.ToUnixTimeMilliseconds() <= now.ToUnixTimeMilliseconds())
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.AssignmentExpiryInvalid);
        }

        string[]? rolePermissions = await repository
            .FindRolePermissionsAsync(roleName, cancellationToken)
            .ConfigureAwait(false);
        if (rolePermissions is null)
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.RoleNotFound);
        }

        if (expiresAtUtc is not null &&
            rolePermissions.Contains(
                AccessControlPermissionGrant.OwnerWildcard,
                StringComparer.Ordinal))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.TemporaryOwnerAssignmentNotAllowed);
        }

        if (!await scopeWriteAdmission.AreOpenAsync(
                [scope],
                cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Unit>(
                AccessControlApplicationErrors.AssignmentRejected);
        }

        if (!await assignmentPolicy.IsAllowedAsync(
                subject,
                roleName,
                scope,
                expiresAtUtc,
                rolePermissions,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.AssignmentRejected);
        }

        AccessControlRoleAssignmentPersistenceOutcome outcome = await repository.TryAssignRoleAsync(
                subject,
                roleName,
                scope,
                expiresAtUtc,
                rolePermissions,
                cancellationToken)
            .ConfigureAwait(false);
        if (outcome == AccessControlRoleAssignmentPersistenceOutcome.RoleDefinitionChanged)
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.AssignmentRejected);
        }

        if (outcome == AccessControlRoleAssignmentPersistenceOutcome.AlreadyExists)
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.AssignmentAlreadyExists);
        }

        if (outcome == AccessControlRoleAssignmentPersistenceOutcome.ExpiryElapsed)
        {
            return Result.Failure<Unit>(AccessControlApplicationErrors.AssignmentExpiryInvalid);
        }

        return Result.Success(Unit.Value);
    }
}
