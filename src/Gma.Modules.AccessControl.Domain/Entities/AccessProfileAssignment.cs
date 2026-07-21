namespace Gma.Modules.AccessControl.Domain.Entities;

using Gma.Framework.Results;
using Gma.Modules.AccessControl.Domain.Errors;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.ValueObjects;

public sealed class AccessProfileAssignment
{
    private AccessProfileAssignment() { }

    private AccessProfileAssignment(
        Guid id,
        Guid profileId,
        AccessProfileAssignmentScope assignmentScope,
        AccessProfileSubject subject,
        AccessProfileSubject actor,
        DateTimeOffset createdAtUtc)
    {
        this.Id = id;
        this.ProfileId = profileId;
        this.AssignmentScopeValue = assignmentScope.Value;
        this.SubjectKind = (int)subject.Kind;
        this.SubjectId = subject.Id;
        this.CreatedByKind = (int)actor.Kind;
        this.CreatedById = actor.Id;
        this.CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid ProfileId { get; private set; }
    public string AssignmentScopeValue { get; private set; } = string.Empty;
    public int SubjectKind { get; private set; }
    public string SubjectId { get; private set; } = string.Empty;
    public int CreatedByKind { get; private set; }
    public string CreatedById { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public AccessProfile? Profile { get; private set; }

    public static Result<AccessProfileAssignment> Create(
        Guid id,
        Guid profileId,
        string? assignmentScopeValue,
        AccessProfileSubject? subject,
        AccessProfileSubject? actor,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<AccessProfileAssignment>(AccessProfileDomainErrors.AssignmentIdRequired);
        }

        if (profileId == Guid.Empty)
        {
            return Result.Failure<AccessProfileAssignment>(AccessProfileDomainErrors.IdRequired);
        }

        Result<AccessProfileAssignmentScope> assignmentScope =
            AccessProfileAssignmentScope.Create(assignmentScopeValue);
        if (assignmentScope.IsFailure)
        {
            return Result.Failure<AccessProfileAssignment>(assignmentScope.Error);
        }

        if (subject is null || actor is null)
        {
            return Result.Failure<AccessProfileAssignment>(AccessProfileDomainErrors.ActorInvalid);
        }

        return Result.Success(new AccessProfileAssignment(
            id,
            profileId,
            assignmentScope.Value,
            subject,
            actor,
            createdAtUtc));
    }
}
