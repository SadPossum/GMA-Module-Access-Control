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

internal sealed class ReconcileAccessProfileAssignmentsCommandHandler(
    IAccessProfileRepository profiles,
    IAccessControlRbacRepository rbac,
    AccessProfilePermissionPolicy permissionPolicy,
    AccessProfileAssignmentPolicy assignmentPolicy,
    IIdGenerator ids,
    ISystemClock clock) : ICommandHandler<ReconcileAccessProfileAssignmentsCommand, AccessProfileAssignmentReconciliationDetails>
{
    private const int MaxProfileCount = 100;

    public async Task<Result<AccessProfileAssignmentReconciliationDetails>> HandleAsync(
        ReconcileAccessProfileAssignmentsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.ProfileIds);
        if (command.OwnerScope.IsGlobal)
        {
            return Result.Failure<AccessProfileAssignmentReconciliationDetails>(
                AccessControlApplicationErrors.ProfileNotFound);
        }

        Result scopeValidation = AccessProfileAssignmentScopePolicy.Validate(
            command.OwnerScope,
            command.AssignmentScope);
        if (scopeValidation.IsFailure)
        {
            return Result.Failure<AccessProfileAssignmentReconciliationDetails>(scopeValidation.Error);
        }

        Guid[] desiredIds = command.ProfileIds.Distinct().Order().ToArray();
        if (desiredIds.Length > MaxProfileCount || desiredIds.Any(id => id == Guid.Empty))
        {
            return Result.Failure<AccessProfileAssignmentReconciliationDetails>(
                AccessControlApplicationErrors.ProfileNotFound);
        }

        IReadOnlyList<AccessProfile> desiredProfiles = await profiles
            .ListTrackedAsync(desiredIds, command.OwnerScope, cancellationToken)
            .ConfigureAwait(false);
        if (desiredProfiles.Count != desiredIds.Length ||
            desiredProfiles.Any(profile => profile.Status == AccessProfileStatus.Archived))
        {
            return Result.Failure<AccessProfileAssignmentReconciliationDetails>(
                AccessControlApplicationErrors.ProfileNotFound);
        }

        foreach (AccessProfile profile in desiredProfiles)
        {
            Result delegation = await permissionPolicy.ValidateDelegationAsync(
                command.Actor,
                command.AssignmentScope,
                profile.Permissions.Select(permission => permission.PermissionCode).ToArray(),
                cancellationToken).ConfigureAwait(false);
            if (delegation.IsFailure)
            {
                return Result.Failure<AccessProfileAssignmentReconciliationDetails>(delegation.Error);
            }

            if (!await assignmentPolicy.IsAllowedAsync(
                    profile,
                    command.OwnerScope,
                    command.AssignmentScope,
                    command.Actor,
                    command.Subject,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return Result.Failure<AccessProfileAssignmentReconciliationDetails>(
                    AccessControlApplicationErrors.ProfileAssignmentRejected);
            }
        }

        IReadOnlyList<AccessProfileAssignment> currentAssignments = await profiles
            .ListTrackedAssignmentsAsync(
                command.Subject,
                command.OwnerScope,
                command.AssignmentScope,
                cancellationToken)
            .ConfigureAwait(false);
        HashSet<Guid> desiredSet = desiredIds.ToHashSet();
        HashSet<Guid> currentIds = currentAssignments.Select(assignment => assignment.ProfileId).ToHashSet();
        AccessProfileAssignment[] removals = currentAssignments
            .Where(assignment => !desiredSet.Contains(assignment.ProfileId))
            .ToArray();
        AccessProfile[] additions = desiredProfiles
            .Where(profile => !currentIds.Contains(profile.Id))
            .ToArray();

        DateTimeOffset nowUtc = clock.UtcNow;
        foreach (AccessProfileAssignment assignment in removals)
        {
            assignment.Profile!.RecordAssignmentChange(
                ids.NewId(),
                AccessProfileChangeKind.Unassigned,
                AccessProfileSubjectMappings.ToDomain(command.Actor),
                AccessProfileSubjectMappings.ToDomain(command.Subject),
                nowUtc,
                assignment.AssignmentScopeValue);
            profiles.RemoveAssignment(assignment);
        }

        if (additions.Length > 0)
        {
            await rbac.EnsureSubjectAsync(command.Subject, nowUtc, cancellationToken).ConfigureAwait(false);
        }

        foreach (AccessProfile profile in additions)
        {
            Result<AccessProfileAssignment> assignment = AccessProfileAssignment.Create(
                ids.NewId(),
                profile.Id,
                command.AssignmentScope.Value,
                AccessProfileSubjectMappings.ToDomain(command.Subject),
                AccessProfileSubjectMappings.ToDomain(command.Actor),
                nowUtc);
            if (assignment.IsFailure)
            {
                return Result.Failure<AccessProfileAssignmentReconciliationDetails>(assignment.Error);
            }

            profiles.AddAssignment(assignment.Value);
            profile.RecordAssignmentChange(
                ids.NewId(),
                AccessProfileChangeKind.Assigned,
                AccessProfileSubjectMappings.ToDomain(command.Actor),
                AccessProfileSubjectMappings.ToDomain(command.Subject),
                nowUtc,
                command.AssignmentScope.Value);
        }

        return Result.Success(new AccessProfileAssignmentReconciliationDetails(
            command.Subject,
            command.OwnerScope,
            desiredIds,
            additions.Length,
            removals.Length));
    }
}
