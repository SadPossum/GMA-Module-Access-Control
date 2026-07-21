namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record ScopedAccessProfileAssignment(
    AccessProfileDto Profile,
    AccessScope AssignmentScope);
