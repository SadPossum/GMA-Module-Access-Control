namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Domain.Aggregates;

internal sealed class ArchiveAccessProfileCommandHandler(
    IAccessProfileRepository repository,
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

        Result archived = profile.Archive(command.ExpectedVersion, command.Actor, ids.NewId(), clock.UtcNow);
        return archived.IsFailure ? Result.Failure<Unit>(archived.Error) : Result.Success(Unit.Value);
    }
}
