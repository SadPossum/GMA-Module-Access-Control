namespace Gma.Modules.AccessControl.Api;

using Gma.Framework.AccessControl;
using Gma.Framework.AccessControl.AspNetCore;
using Microsoft.AspNetCore.Http;

internal sealed class AccessProfileScopeResolver : IAccessHttpScopeResolver
{
    public const string ResolverName = "access-control-profile";

    public string Name => ResolverName;

    public ValueTask<AccessScopeResolutionResult> ResolveAsync(
        HttpContext httpContext,
        AccessPermissionMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        string value = httpContext.Request.Query["scope"].ToString();
        return ValueTask.FromResult(
            AccessScope.TryParse(value, out AccessScope? scope) && !scope.IsGlobal
                ? AccessScopeResolutionResult.Success(scope)
                : AccessScopeResolutionResult.Failure(
                    "AccessControl.ProfileScopeInvalid",
                    "A valid non-global access-profile scope is required.",
                    StatusCodes.Status400BadRequest));
    }
}
