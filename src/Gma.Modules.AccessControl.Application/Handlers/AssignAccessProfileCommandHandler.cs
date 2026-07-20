namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Domain.Enums;

internal sealed class AssignAccessProfileCommandHandler(
    IAccessProfileRepository profiles,
    IAccessControlRbacRepository rbac,
    AccessProfilePermissionPolicy permissionPolicy,
    AccessProfileAssignmentPolicy assignmentPolicy,
    IIdGenerator ids,
    ISystemClock clock) : ICommandHandler<AssignAccessProfileCommand, AccessProfileAssignmentDetails>
{
    public async Task<Result<AccessProfileAssignmentDetails>> HandleAsync(
        AssignAccessProfileCommand command,
        CancellationToken cancellationToken)
    {
        AccessProfile? profile = await profiles
            .GetAsync(command.ProfileId, command.OwnerScope, tracking: true, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null) return Result.Failure<AccessProfileAssignmentDetails>(AccessControlApplicationErrors.ProfileNotFound);
        if (profile.Status == AccessProfileStatus.Archived)
        {
            return Result.Failure<AccessProfileAssignmentDetails>(AccessControlApplicationErrors.ProfileNotFound);
        }

        Result delegation = await permissionPolicy.ValidateDelegationAsync(
            command.Actor,
            command.OwnerScope,
            profile.Permissions.Select(permission => permission.PermissionCode).ToArray(),
            cancellationToken).ConfigureAwait(false);
        if (delegation.IsFailure)
        {
            return Result.Failure<AccessProfileAssignmentDetails>(delegation.Error);
        }

        if (!await assignmentPolicy.IsAllowedAsync(
                profile,
                command.OwnerScope,
                command.Actor,
                command.Subject,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return Result.Failure<AccessProfileAssignmentDetails>(
                AccessControlApplicationErrors.ProfileAssignmentRejected);
        }

        if (await profiles.AssignmentExistsAsync(profile.Id, command.Subject, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<AccessProfileAssignmentDetails>(AccessControlApplicationErrors.ProfileAssignmentAlreadyExists);
        }

        DateTimeOffset nowUtc = clock.UtcNow;
        Result<AccessProfileAssignment> assignment = AccessProfileAssignment.Create(
            ids.NewId(), profile.Id,
            AccessProfileSubjectMappings.ToDomain(command.Subject),
            AccessProfileSubjectMappings.ToDomain(command.Actor),
            nowUtc);
        if (assignment.IsFailure) return Result.Failure<AccessProfileAssignmentDetails>(assignment.Error);

        await rbac.EnsureSubjectAsync(command.Subject, nowUtc, cancellationToken).ConfigureAwait(false);
        profiles.AddAssignment(assignment.Value);
        profile.RecordAssignmentChange(
            ids.NewId(), AccessProfileChangeKind.Assigned,
            AccessProfileSubjectMappings.ToDomain(command.Actor),
            AccessProfileSubjectMappings.ToDomain(command.Subject),
            nowUtc);
        return Result.Success(new AccessProfileAssignmentDetails(
            assignment.Value.Id, profile.Id, command.Subject.Kind, command.Subject.Id,
            command.Actor.Kind, command.Actor.Id, nowUtc));
    }
}
