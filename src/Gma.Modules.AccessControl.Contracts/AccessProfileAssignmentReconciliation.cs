namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessProfileAssignmentReconciliation(
    AccessSubject Subject,
    AccessScope OwnerScope,
    IReadOnlyList<Guid> ProfileIds,
    int AssignedCount,
    int UnassignedCount);
