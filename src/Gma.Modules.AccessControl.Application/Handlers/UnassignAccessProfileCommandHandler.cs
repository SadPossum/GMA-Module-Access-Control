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

internal sealed class UnassignAccessProfileCommandHandler(
    IAccessProfileRepository repository,
    IIdGenerator ids,
    ISystemClock clock) : ICommandHandler<UnassignAccessProfileCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        UnassignAccessProfileCommand command,
        CancellationToken cancellationToken)
    {
        AccessProfile? profile = await repository
            .GetAsync(command.ProfileId, command.OwnerScope, tracking: true, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null) return Result.Failure<Unit>(AccessControlApplicationErrors.ProfileNotFound);

        AccessProfileAssignment? assignment = await repository
            .GetAssignmentAsync(profile.Id, command.Subject, cancellationToken)
            .ConfigureAwait(false);
        if (assignment is null) return Result.Failure<Unit>(AccessControlApplicationErrors.ProfileAssignmentNotFound);

        repository.RemoveAssignment(assignment);
        profile.RecordAssignmentChange(
            ids.NewId(), AccessProfileChangeKind.Unassigned, command.Actor, command.Subject, clock.UtcNow);
        return Result.Success(Unit.Value);
    }
}
