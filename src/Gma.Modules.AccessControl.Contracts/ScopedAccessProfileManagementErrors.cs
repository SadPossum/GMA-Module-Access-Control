namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.Results;

public static class ScopedAccessProfileManagementErrors
{
    public static readonly Error AccessDenied = new(
        "AccessControl.ScopedProfileManagementAccessDenied",
        "The actor is not authorized to manage scoped access-profile assignments.");

    public static readonly Error AssignmentRejected = new(
        "AccessControl.ScopedProfileAssignmentRejected",
        "The requested scoped access-profile assignment is not allowed.");

    public static readonly Error PermissionEscalation = new(
        "AccessControl.ScopedProfilePermissionEscalation",
        "The actor cannot delegate a permission they do not hold at the assignment scope.");

    public static readonly Error ScopeInvalid = new(
        "AccessControl.ScopedProfileAssignmentScopeInvalid",
        "Every assignment scope must equal or descend from the profile owner scope.");

    public static readonly Error ProfileUnavailable = new(
        "AccessControl.ScopedProfileUnavailable",
        "One or more requested access profiles are unavailable in the owning scope.");
}
