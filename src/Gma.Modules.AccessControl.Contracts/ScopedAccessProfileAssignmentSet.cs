namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record ScopedAccessProfileAssignmentSet(
    AccessSubject Subject,
    AccessScope OwnerScope,
    IReadOnlyList<ScopedAccessProfileAssignment> Assignments);
