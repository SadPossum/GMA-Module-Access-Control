namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record ScopedAccessProfileAssignmentReconciliation(
    AccessSubject Subject,
    AccessScope OwnerScope,
    IReadOnlyList<AccessProfileAssignmentTarget> Targets,
    int AssignedCount,
    int UnassignedCount);
