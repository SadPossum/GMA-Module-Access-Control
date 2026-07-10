namespace Gma.Modules.AccessControl.AdminApi;

using System.Text.Json.Serialization;
using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Admin.Contracts;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Queries;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Gma.Framework.Administration;
using Gma.Framework.Administration.AccessControl;
using Gma.Framework.Administration.Api;
using Gma.Framework.Api.Observability;
using Gma.Framework.Api.Results;
using Gma.Framework.Cqrs;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Results;

public sealed class AccessControlAdminApiModule : IAdminApiModule
{
    public string Name => AccessControlModuleMetadata.Name;

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.SelectModuleProfile(AccessControlProfiles.Default, "Gma.Modules.AccessControl.AdminApi");
        builder.Services.AddGmaAccessControlAdministrationAuthorization();
        builder.Services.AddAccessControlApplication(builder.Configuration);
        builder.AddAccessControlPersistence();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/admin")
            .WithModuleName(this.Name)
            .WithTags("AccessControl Admin")
            .RequireAuthorization();

        RouteGroupBuilder roles = group.MapGroup("/roles");

        roles.MapGet("/", async (
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(AccessControlAdminOperationNames.RolesList, AccessControlAdminPermissions.RolesRead),
                requireTenant: false,
                token => dispatcher.QueryAsync(new ListRolesQuery(), token),
                cancellationToken).ConfigureAwait(false));

        roles.MapPost("/", async (
            CreateAccessRoleRequest request,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(AccessControlAdminOperationNames.RolesCreate, AccessControlAdminPermissions.RolesManage),
                requireTenant: false,
                token => dispatcher.SendAsync(new CreateRoleCommand(request.RoleName), token),
                cancellationToken,
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));

        roles.MapPost("/{roleName}/permissions", async (
            string roleName,
            GrantAccessRolePermissionRequest request,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(AccessControlAdminOperationNames.RolesGrant, AccessControlAdminPermissions.RolesManage),
                requireTenant: false,
                token => dispatcher.SendAsync(new GrantRolePermissionCommand(roleName, request.PermissionCode), token),
                cancellationToken,
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));

        roles.MapPost("/{roleName}/assignments", async (
            string roleName,
            AssignAccessRoleRequest request,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(AccessControlAdminOperationNames.RolesAssign, AccessControlAdminPermissions.RolesManage),
                requireTenant: false,
                token => SendAssignRoleCommandAsync(dispatcher, request, roleName, token),
                cancellationToken,
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));
    }

    public sealed record CreateAccessRoleRequest([property: JsonPropertyName("name")] string RoleName);
    public sealed record GrantAccessRolePermissionRequest([property: JsonPropertyName("permission")] string PermissionCode);
    public sealed record AssignAccessRoleRequest(string ActorId, [property: JsonPropertyName("scope")] string? AccessScope);

    private static Task<Result<Unit>> SendAssignRoleCommandAsync(
        IRequestDispatcher dispatcher,
        AssignAccessRoleRequest request,
        string roleName,
        CancellationToken cancellationToken)
    {
        if (!TryParseScope(request.AccessScope, out AccessScope? scope))
        {
            return Task.FromResult(Result.Failure<Unit>(AccessControlApplicationErrors.ScopeInvalid));
        }

        return dispatcher.SendAsync(new AssignRoleCommand(request.ActorId, roleName, scope), cancellationToken);
    }

    private static bool TryParseScope(string? value, out AccessScope? scope)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            scope = null;
            return true;
        }

        if (AccessScope.TryParse(value, out AccessScope? parsedScope))
        {
            scope = parsedScope;
            return true;
        }

        scope = null;
        return false;
    }

    private static readonly ApiErrorStatusCodeMap AdminErrorStatusCodes = ApiErrorStatusCodeMap.Create(
        new(AccessControlApplicationErrors.RoleNotFound.Code, StatusCodes.Status404NotFound),
        new(AccessControlApplicationErrors.RoleAlreadyExists.Code, StatusCodes.Status409Conflict),
        new(AccessControlApplicationErrors.PermissionAlreadyGranted.Code, StatusCodes.Status409Conflict),
        new(AccessControlApplicationErrors.AssignmentAlreadyExists.Code, StatusCodes.Status409Conflict));
}
