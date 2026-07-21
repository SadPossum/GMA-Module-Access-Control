namespace Gma.Modules.AccessControl.Tests;

using System.Text.Json;
using Gma.Framework.AccessControl;
using Gma.Framework.AccessControl.AspNetCore;
using Gma.Framework.Cqrs;
using Gma.Framework.Permissions;
using Gma.Framework.Security;
using Gma.Modules.AccessControl.Api;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessProfileApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Configured_assurance_protects_profile_mutations_only()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.Configure<AccessControlApiSecurityOptions>(options =>
            options.ProfileManagementAssurance = new AuthenticationAssuranceRequirement(
                maxAuthenticationAge: TimeSpan.FromMinutes(10)));
        builder.Services.AddSingleton<IRequestDispatcher>(_ => null!);
        builder.Services.AddSingleton<IAccessHttpSubjectResolver>(_ => null!);
        await using WebApplication app = builder.Build();

        new AccessControlApiModule().MapEndpoints(app);

        RouteEndpoint[] endpoints = [.. ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()];
        AssertAssurance(endpoints, HttpMethods.Post, "/api/access-control/profiles/", expected: true);
        AssertAssurance(endpoints, HttpMethods.Put, "/api/access-control/profiles/{profileId:guid}", expected: true);
        AssertAssurance(endpoints, HttpMethods.Post, "/api/access-control/profiles/{profileId:guid}/assignments", expected: true);
        AssertAssurance(endpoints, HttpMethods.Delete, "/api/access-control/profiles/{profileId:guid}/assignments", expected: true);
        AssertAssurance(endpoints, HttpMethods.Get, "/api/access-control/profiles/", expected: false);
        AssertAssurance(endpoints, HttpMethods.Get, "/api/access-control/profiles/{profileId:guid}/history", expected: false);
    }

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

    [Fact]
    public void Api_mapping_exposes_assignment_scope_in_assignments_and_history()
    {
        AccessScope assignmentScope =
            AccessScope.Parse("tenant:tenant-a/property:property-a");
        AccessProfileAssignmentDto assignment = AccessProfileApiMappings.ToDto(
            new AccessProfileAssignmentDetails(
                Guid.NewGuid(),
                Guid.NewGuid(),
                AccessSubjectKind.User,
                "member-a",
                AccessSubjectKind.User,
                "owner-a",
                DateTimeOffset.UtcNow,
                assignmentScope));
        AccessProfileChangeDto change = AccessProfileApiMappings.ToDto(
            new AccessProfileChangeDetails(
                Guid.NewGuid(),
                assignment.ProfileId,
                AccessProfileChangeKind.Assigned,
                AccessSubjectKind.User,
                "owner-a",
                AccessSubjectKind.User,
                "member-a",
                1,
                DateTimeOffset.UtcNow,
                assignmentScope));

        Assert.Equal(assignmentScope.Value, assignment.AssignmentScope);
        Assert.Equal(assignmentScope.Value, change.AssignmentScope);
    }

    private static void AssertAssurance(
        IEnumerable<RouteEndpoint> endpoints,
        string method,
        string route,
        bool expected)
    {
        RouteEndpoint endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText, route, StringComparison.Ordinal) &&
            candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        bool configured = endpoint.Metadata.Any(metadata =>
            string.Equals(metadata.GetType().Name, "AuthenticationAssuranceMetadata", StringComparison.Ordinal));
        Assert.Equal(expected, configured);
    }
}
