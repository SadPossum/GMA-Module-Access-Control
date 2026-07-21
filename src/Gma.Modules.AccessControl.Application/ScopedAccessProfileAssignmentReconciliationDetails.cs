namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Contracts;

public sealed record ScopedAccessProfileAssignmentReconciliationDetails(
    AccessSubject Subject,
    AccessScope OwnerScope,
    IReadOnlyList<AccessProfileAssignmentTarget> Targets,
    int AssignedCount,
    int UnassignedCount);
