namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using DomainChangeKind = Gma.Modules.AccessControl.Domain.Enums.AccessProfileChangeKind;
using DomainStatus = Gma.Modules.AccessControl.Domain.Enums.AccessProfileStatus;

internal sealed class ReconcileScopedAccessProfileAssignmentsCommandHandler(
    IAccessProfileRepository profiles,
    IAccessControlRbacRepository rbac,
    AccessProfilePermissionPolicy permissionPolicy,
    AccessProfileAssignmentPolicy assignmentPolicy,
    IIdGenerator ids,
    ISystemClock clock)
    : ICommandHandler<ReconcileScopedAccessProfileAssignmentsCommand, ScopedAccessProfileAssignmentReconciliationDetails>
{
    private const int MaxTargetCount = 500;

    public async Task<Result<ScopedAccessProfileAssignmentReconciliationDetails>> HandleAsync(
        ReconcileScopedAccessProfileAssignmentsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Targets);
        if (command.OwnerScope.IsGlobal || command.Targets.Count > MaxTargetCount)
        {
            return Failure(AccessControlApplicationErrors.ProfileNotFound);
        }

        if (command.Targets.Any(target => target is null || target.AssignmentScope is null))
        {
            return Failure(AccessControlApplicationErrors.ProfileAssignmentScopeInvalid);
        }

        AccessProfileAssignmentTarget[] desiredTargets = command.Targets
            .DistinctBy(target => (target.ProfileId, target.AssignmentScope.Value))
            .OrderBy(target => target.AssignmentScope.Value, StringComparer.Ordinal)
            .ThenBy(target => target.ProfileId)
            .ToArray();
        if (desiredTargets.Any(target => target.ProfileId == Guid.Empty ||
                                         AccessProfileAssignmentScopePolicy.Validate(
                                             command.OwnerScope,
                                             target.AssignmentScope).IsFailure))
        {
            return Failure(AccessControlApplicationErrors.ProfileAssignmentScopeInvalid);
        }

        Guid[] desiredProfileIds = desiredTargets.Select(target => target.ProfileId).Distinct().ToArray();
        IReadOnlyList<AccessProfile> desiredProfiles = await profiles
            .ListTrackedAsync(desiredProfileIds, command.OwnerScope, cancellationToken)
            .ConfigureAwait(false);
        if (desiredProfiles.Count != desiredProfileIds.Length ||
            desiredProfiles.Any(profile => profile.Status == DomainStatus.Archived))
        {
            return Failure(AccessControlApplicationErrors.ProfileNotFound);
        }

        Dictionary<Guid, AccessProfile> profilesById = desiredProfiles.ToDictionary(profile => profile.Id);
        foreach (IGrouping<string, AccessProfileAssignmentTarget> scopeTargets in desiredTargets
                     .GroupBy(target => target.AssignmentScope.Value, StringComparer.Ordinal))
        {
            AccessScope assignmentScope = scopeTargets.First().AssignmentScope;
            string[] permissions = scopeTargets
                .SelectMany(target => profilesById[target.ProfileId].Permissions)
                .Select(permission => permission.PermissionCode)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Result delegation = await permissionPolicy.ValidateDelegationAsync(
                command.Actor,
                assignmentScope,
                permissions,
                cancellationToken).ConfigureAwait(false);
            if (delegation.IsFailure)
            {
                return Failure(delegation.Error);
            }

            foreach (AccessProfileAssignmentTarget target in scopeTargets)
            {
                if (!await assignmentPolicy.IsAllowedAsync(
                        profilesById[target.ProfileId],
                        command.OwnerScope,
                        target.AssignmentScope,
                        command.Actor,
                        command.Subject,
                        cancellationToken).ConfigureAwait(false))
                {
                    return Failure(AccessControlApplicationErrors.ProfileAssignmentRejected);
                }
            }
        }

        IReadOnlyList<AccessProfileAssignment> currentAssignments = await profiles
            .ListTrackedAssignmentsAsync(command.Subject, command.OwnerScope, null, cancellationToken)
            .ConfigureAwait(false);
        HashSet<(Guid ProfileId, string Scope)> desiredKeys = desiredTargets
            .Select(target => (target.ProfileId, target.AssignmentScope.Value))
            .ToHashSet();
        HashSet<(Guid ProfileId, string Scope)> currentKeys = currentAssignments
            .Select(assignment => (assignment.ProfileId, assignment.AssignmentScopeValue))
            .ToHashSet();
        AccessProfileAssignment[] removals = currentAssignments
            .Where(assignment => !desiredKeys.Contains((assignment.ProfileId, assignment.AssignmentScopeValue)))
            .ToArray();
        AccessProfileAssignmentTarget[] additions = desiredTargets
            .Where(target => !currentKeys.Contains((target.ProfileId, target.AssignmentScope.Value)))
            .ToArray();

        DateTimeOffset nowUtc = clock.UtcNow;
        foreach (AccessProfileAssignment assignment in removals)
        {
            assignment.Profile!.RecordAssignmentChange(
                ids.NewId(),
                DomainChangeKind.Unassigned,
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

        foreach (AccessProfileAssignmentTarget target in additions)
        {
            AccessProfile profile = profilesById[target.ProfileId];
            Result<AccessProfileAssignment> assignment = AccessProfileAssignment.Create(
                ids.NewId(),
                profile.Id,
                target.AssignmentScope.Value,
                AccessProfileSubjectMappings.ToDomain(command.Subject),
                AccessProfileSubjectMappings.ToDomain(command.Actor),
                nowUtc);
            if (assignment.IsFailure)
            {
                return Failure(assignment.Error);
            }

            profiles.AddAssignment(assignment.Value);
            profile.RecordAssignmentChange(
                ids.NewId(),
                DomainChangeKind.Assigned,
                AccessProfileSubjectMappings.ToDomain(command.Actor),
                AccessProfileSubjectMappings.ToDomain(command.Subject),
                nowUtc,
                target.AssignmentScope.Value);
        }

        return Result.Success(new ScopedAccessProfileAssignmentReconciliationDetails(
            command.Subject,
            command.OwnerScope,
            desiredTargets,
            additions.Length,
            removals.Length));
    }

    private static Result<ScopedAccessProfileAssignmentReconciliationDetails> Failure(Error error) =>
        Result.Failure<ScopedAccessProfileAssignmentReconciliationDetails>(error);
}
