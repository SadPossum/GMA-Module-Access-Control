namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.ValueObjects;

internal sealed class CreateAccessProfileCommandHandler(
    IAccessProfileRepository repository,
    AccessProfilePermissionPolicy permissionPolicy,
    IIdGenerator ids,
    ISystemClock clock) : ICommandHandler<CreateAccessProfileCommand, AccessProfileDetails>
{
    public async Task<Result<AccessProfileDetails>> HandleAsync(
        CreateAccessProfileCommand command,
        CancellationToken cancellationToken)
    {
        Result delegation = await permissionPolicy.ValidateDelegationAsync(
            command.Actor, command.OwnerScope, command.Permissions, cancellationToken).ConfigureAwait(false);
        if (delegation.IsFailure) return Result.Failure<AccessProfileDetails>(delegation.Error);

        Result<AccessProfileKey> key = AccessProfileKey.Create(command.Key);
        if (key.IsFailure) return Result.Failure<AccessProfileDetails>(key.Error);
        if (await repository.KeyExistsAsync(command.OwnerScope, key.Value.Value, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<AccessProfileDetails>(AccessControlApplicationErrors.ProfileAlreadyExists);
        }

        Result<AccessProfile> profile = AccessProfile.Create(
            ids.NewId(), command.OwnerScope.Value, key.Value.Value, command.DisplayName, command.Description,
            command.Permissions, AccessProfileSubjectMappings.ToDomain(command.Actor), ids.NewId(), clock.UtcNow);
        if (profile.IsFailure) return Result.Failure<AccessProfileDetails>(profile.Error);

        repository.Add(profile.Value);
        return Result.Success(AccessProfileMappings.ToDetails(profile.Value, assignmentCount: 0));
    }
}
