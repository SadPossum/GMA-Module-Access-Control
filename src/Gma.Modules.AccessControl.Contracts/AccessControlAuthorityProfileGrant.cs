namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessControlAuthorityProfileGrant(
    Guid AssignmentId,
    AccessScope AssignmentScope,
    Guid ProfileId,
    AccessScope OwnerScope,
    string ProfileKey,
    AccessProfileStatus ProfileStatus,
    long ProfileVersion,
    IReadOnlyList<string> Permissions);
