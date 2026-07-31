namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.ValueObjects;

internal sealed class EnsureAccessProfileCommandHandler(
    IAccessProfileRepository repository,
    AccessProfilePermissionPolicy permissionPolicy,
    AccessProfileMutationAdmissionPolicy mutationAdmission,
    IIdGenerator ids,
    ISystemClock clock) : ICommandHandler<EnsureAccessProfileCommand, AccessProfileDetails>
{
    public async Task<Result<AccessProfileDetails>> HandleAsync(
        EnsureAccessProfileCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Definition);
        if (command.OwnerScope.IsGlobal)
        {
            return Result.Failure<AccessProfileDetails>(AccessControlApplicationErrors.ProfileNotFound);
        }

        Result<AccessProfileKey> key = AccessProfileKey.Create(command.Definition.Key);
        if (key.IsFailure)
        {
            return Result.Failure<AccessProfileDetails>(key.Error);
        }

        AccessProfileDetails? existing = await repository
            .GetDetailsByKeyAsync(command.OwnerScope, key.Value.Value, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result.Success(existing);
        }

        Result admitted = await mutationAdmission.AuthorizeAsync(
            new AccessProfileMutationAdmissionContext(
                AccessProfileMutationAdmissionOperation.EnsureProfile,
                command.OwnerScope,
                command.Actor,
                ProfileKey: key.Value.Value),
            cancellationToken).ConfigureAwait(false);
        if (admitted.IsFailure)
        {
            return Result.Failure<AccessProfileDetails>(admitted.Error);
        }

        Result delegation = await permissionPolicy.ValidateDelegationAsync(
            command.Actor,
            command.OwnerScope,
            command.Definition.Permissions,
            cancellationToken).ConfigureAwait(false);
        if (delegation.IsFailure)
        {
            return Result.Failure<AccessProfileDetails>(delegation.Error);
        }

        Result<AccessProfile> profile = AccessProfile.Create(
            ids.NewId(),
            command.OwnerScope.Value,
            key.Value.Value,
            command.Definition.DisplayName,
            command.Definition.Description,
            command.Definition.Permissions,
            AccessProfileSubjectMappings.ToDomain(command.Actor),
            ids.NewId(),
            clock.UtcNow);
        if (profile.IsFailure)
        {
            return Result.Failure<AccessProfileDetails>(profile.Error);
        }

        repository.Add(profile.Value);
        return Result.Success(AccessProfileMappings.ToDetails(profile.Value, assignmentCount: 0));
    }
}
