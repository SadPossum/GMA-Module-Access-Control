namespace Gma.Modules.AccessControl.Tests;

using System.Text.Json;
using Gma.Framework.AccessControl;
using Gma.Framework.AccessControl.AspNetCore;
using Gma.Framework.Permissions;
using Gma.Modules.AccessControl.Api;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Contracts;
using Microsoft.AspNetCore.Http;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessProfileApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
    public void Api_mapping_exposes_stable_contract_enum_wire_values()
    {
        AccessProfileDetails details = new(
            Guid.NewGuid(),
            AccessScope.Parse("tenant:tenant-a"),
            "front-desk",
            "Front desk",
            string.Empty,
            AccessProfileStatus.Active,
            3,
            ["reservations.read"],
            2,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        AccessProfileDto dto = AccessProfileApiMappings.ToDto(details);
        string json = JsonSerializer.Serialize(dto, JsonOptions);

        Assert.Equal("tenant:tenant-a", dto.OwnerScope);
        Assert.Equal(AccessProfileStatus.Active, dto.Status);
        Assert.Contains("\"status\":\"active\"", json, StringComparison.Ordinal);
        Assert.Equal(["reservations.read"], dto.Permissions);
    }
}
