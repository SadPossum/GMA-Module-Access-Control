namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;

internal sealed record ScopedAccessProfileAssignmentDetails(
    AccessProfileDetails Profile,
    AccessScope AssignmentScope);
