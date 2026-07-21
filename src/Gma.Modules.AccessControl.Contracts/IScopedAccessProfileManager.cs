namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;
using Gma.Framework.Results;

public interface IScopedAccessProfileManager
{
    Task<Result<ScopedAccessProfileAssignmentSet>> GetSubjectAssignmentsAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        AccessSubject actor,
        CancellationToken cancellationToken = default);

    Task<Result<ScopedAccessProfileAssignmentReconciliation>> ReconcileSubjectAssignmentsAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        IReadOnlyCollection<AccessProfileAssignmentTarget> targets,
        AccessSubject actor,
        CancellationToken cancellationToken = default);
}
