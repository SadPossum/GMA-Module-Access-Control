namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;

public sealed record AccessProfileAssignmentReconciliationDetails(
    AccessSubject Subject,
    AccessScope OwnerScope,
    IReadOnlyList<Guid> ProfileIds,
    int AssignedCount,
    int UnassignedCount);
