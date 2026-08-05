namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;

internal sealed class UpdateAccessProfileCommandHandler(
    IAccessProfileRepository repository,
    AccessProfilePermissionPolicy permissionPolicy,
    AccessControlScopeWriteAdmission scopeWriteAdmission,
    AccessProfileMutationAdmissionPolicy mutationAdmission,
    IIdGenerator ids,
    ISystemClock clock) : ICommandHandler<UpdateAccessProfileCommand, AccessProfileDetails>
{
    public async Task<Result<AccessProfileDetails>> HandleAsync(
        UpdateAccessProfileCommand command,
        CancellationToken cancellationToken)
    {
        Result delegation = await permissionPolicy.ValidateDelegationAsync(
            command.Actor, command.OwnerScope, command.Permissions, cancellationToken).ConfigureAwait(false);
        if (delegation.IsFailure) return Result.Failure<AccessProfileDetails>(delegation.Error);

        AccessProfile? profile = await repository
            .GetAsync(command.ProfileId, command.OwnerScope, tracking: true, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null) return Result.Failure<AccessProfileDetails>(AccessControlApplicationErrors.ProfileNotFound);

        if (!await scopeWriteAdmission.AreOpenAsync(
                [command.OwnerScope],
                cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<AccessProfileDetails>(
                AccessControlApplicationErrors.ProfileMutationRejected);
        }

        Result admitted = await mutationAdmission.AuthorizeAsync(
            new AccessProfileMutationAdmissionContext(
                AccessProfileMutationAdmissionOperation.UpdateProfile,
                command.OwnerScope,
                command.Actor,
                profile.Id,
                profile.Key),
            cancellationToken).ConfigureAwait(false);
        if (admitted.IsFailure)
        {
            return Result.Failure<AccessProfileDetails>(admitted.Error);
        }

        Result updated = profile.Update(
            command.DisplayName, command.Description, command.Permissions, command.ExpectedVersion,
            AccessProfileSubjectMappings.ToDomain(command.Actor), ids.NewId(), clock.UtcNow);
        if (updated.IsFailure) return Result.Failure<AccessProfileDetails>(updated.Error);

        int assignments = await repository.CountAssignmentsAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success(AccessProfileMappings.ToDetails(profile, assignments));
    }
}
