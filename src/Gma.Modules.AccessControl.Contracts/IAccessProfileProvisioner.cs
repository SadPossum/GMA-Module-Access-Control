namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public interface IAccessProfileProvisioner
{
    Task<AccessProfileDto> EnsureProfileAsync(
        AccessScope ownerScope,
        AccessProfileDefinition definition,
        AccessSubject actor,
        CancellationToken cancellationToken = default);

    Task<AccessProfileDto?> FindProfileByKeyAsync(
        AccessScope ownerScope,
        string key,
        CancellationToken cancellationToken = default);

    Task<AccessProfileAssignmentSet> GetSubjectAssignmentsAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        CancellationToken cancellationToken = default);

    Task<AccessProfileAssignmentReconciliation> ReconcileSubjectAssignmentsAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        IReadOnlyCollection<Guid> profileIds,
        AccessSubject actor,
        CancellationToken cancellationToken = default);
}
