namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Framework.Results;

internal static class AccessProfileAssignmentScopePolicy
{
    private static readonly AccessScopeMatchOptions DescendantOrExact = new(
        AllowAncestorScopeGrants: true);

    public static Result Validate(AccessScope ownerScope, AccessScope assignmentScope)
    {
        ArgumentNullException.ThrowIfNull(ownerScope);
        ArgumentNullException.ThrowIfNull(assignmentScope);
        return !ownerScope.IsGlobal &&
               !assignmentScope.IsGlobal &&
               AccessScopeMatcher.GrantSatisfiesRequest(ownerScope, assignmentScope, DescendantOrExact)
            ? Result.Success()
            : Result.Failure(AccessControlApplicationErrors.ProfileAssignmentScopeInvalid);
    }
}
