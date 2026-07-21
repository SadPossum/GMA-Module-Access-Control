namespace Gma.Modules.AccessControl.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Domain.Enums;

internal sealed class RevokeAccessProfileAssignmentsCommandHandler(
    IAccessProfileRepository repository,
    IIdGenerator ids,
    ISystemClock clock) : ICommandHandler<RevokeAccessProfileAssignmentsCommand, int>
{
    public async Task<Result<int>> HandleAsync(
        RevokeAccessProfileAssignmentsCommand command,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AccessProfileAssignment> assignments = await repository
            .ListTrackedAssignmentsAsync(command.Subject, command.OwnerScope, null, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset nowUtc = clock.UtcNow;
        foreach (AccessProfileAssignment assignment in assignments)
        {
            assignment.Profile!.RecordAssignmentChange(
                ids.NewId(),
                AccessProfileChangeKind.Unassigned,
                AccessProfileSubjectMappings.ToDomain(command.Actor),
                AccessProfileSubjectMappings.ToDomain(command.Subject),
                nowUtc,
                assignment.AssignmentScopeValue);
            repository.RemoveAssignment(assignment);
        }

        return Result.Success(assignments.Count);
    }
}
