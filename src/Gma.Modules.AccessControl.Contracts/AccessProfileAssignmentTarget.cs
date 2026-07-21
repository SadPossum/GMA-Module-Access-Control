namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessProfileAssignmentTarget(
    Guid ProfileId,
    AccessScope AssignmentScope);
