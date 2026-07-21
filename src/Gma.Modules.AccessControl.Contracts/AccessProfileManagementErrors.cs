namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.Results;

public static class AccessProfileManagementErrors
{
    public static readonly Error AccessDenied = new(
        "AccessControl.ProfileManagementAccessDenied",
        "The actor is not authorized to manage access profiles in this scope.");
}
