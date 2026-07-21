namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public interface IScopedAccessProfileProvisioner
{
    Task<ScopedAccessProfileAssignmentSet> GetSubjectScopedAssignmentsAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        CancellationToken cancellationToken = default);

    Task<ScopedAccessProfileAssignmentReconciliation> ReconcileSubjectScopedAssignmentsAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        IReadOnlyCollection<AccessProfileAssignmentTarget> targets,
        AccessSubject actor,
        CancellationToken cancellationToken = default);
}
