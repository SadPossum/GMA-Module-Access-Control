namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.AccessControl.AspNetCore;
using Gma.Framework.Permissions;
using Gma.Modules.AccessControl.Api;
using Gma.Modules.AccessControl.Application;
using Microsoft.AspNetCore.Http;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessProfileApiTests
{
    [Fact]
    public async Task Scope_resolver_accepts_only_non_global_profile_scopes()
    {
        AccessProfileScopeResolver resolver = new();
        AccessPermissionMetadata metadata = new(PermissionCode.Create("access-control.profiles.read"));
        DefaultHttpContext validContext = new();
        validContext.Request.QueryString = new QueryString("?scope=tenant:tenant-a");
        DefaultHttpContext globalContext = new();
        globalContext.Request.QueryString = new QueryString("?scope=global");

        AccessScopeResolutionResult valid = await resolver.ResolveAsync(
            validContext, metadata, CancellationToken.None);
        AccessScopeResolutionResult global = await resolver.ResolveAsync(
            globalContext, metadata, CancellationToken.None);

        Assert.True(valid.IsSuccess);
        Assert.Equal("tenant:tenant-a", valid.Scope?.Value);
        Assert.False(global.IsSuccess);
        Assert.Equal(StatusCodes.Status400BadRequest, global.StatusCode);
        Assert.Equal("AccessControl.ProfileScopeInvalid", global.ErrorCode);
    }

    [Fact]
    public void Api_mapping_exposes_stable_string_values_instead_of_domain_types()
    {
        AccessProfileDetails details = new(
            Guid.NewGuid(),
            AccessScope.Parse("tenant:tenant-a"),
            "front-desk",
            "Front desk",
            string.Empty,
            "active",
            3,
            ["reservations.read"],
            2,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Gma.Modules.AccessControl.Contracts.AccessProfileDto dto = AccessProfileApiMappings.ToDto(details);

        Assert.Equal("tenant:tenant-a", dto.OwnerScope);
        Assert.Equal("active", dto.Status);
        Assert.Equal(["reservations.read"], dto.Permissions);
    }
}
