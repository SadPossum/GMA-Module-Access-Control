namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;

internal sealed class ArchiveAccessProfileCommandHandler(
    IAccessProfileRepository repository,
    AccessProfileMutationAdmissionPolicy mutationAdmission,
    IIdGenerator ids,
    ISystemClock clock) : ICommandHandler<ArchiveAccessProfileCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        ArchiveAccessProfileCommand command,
        CancellationToken cancellationToken)
    {
        AccessProfile? profile = await repository
            .GetAsync(command.ProfileId, command.OwnerScope, tracking: true, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null) return Result.Failure<Unit>(AccessControlApplicationErrors.ProfileNotFound);

        Result admitted = await mutationAdmission.AuthorizeAsync(
            new AccessProfileMutationAdmissionContext(
                AccessProfileMutationAdmissionOperation.ArchiveProfile,
                command.OwnerScope,
                command.Actor,
                profile.Id,
                profile.Key),
            cancellationToken).ConfigureAwait(false);
        if (admitted.IsFailure)
        {
            return Result.Failure<Unit>(admitted.Error);
        }

        Result archived = profile.Archive(
            command.ExpectedVersion,
            AccessProfileSubjectMappings.ToDomain(command.Actor),
            ids.NewId(),
            clock.UtcNow);
        return archived.IsFailure ? Result.Failure<Unit>(archived.Error) : Result.Success(Unit.Value);
    }
}
