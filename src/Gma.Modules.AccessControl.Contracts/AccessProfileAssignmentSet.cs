namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessProfileAssignmentSet(
    AccessSubject Subject,
    AccessScope OwnerScope,
    IReadOnlyList<AccessProfileDto> Profiles);
