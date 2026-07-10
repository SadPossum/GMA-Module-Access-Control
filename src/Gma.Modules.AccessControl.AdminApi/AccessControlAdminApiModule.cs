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

        roles.MapDelete("/{roleName}/permissions/{permissionCode}", async (
            string roleName,
            string permissionCode,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(AccessControlAdminOperationNames.RolesRevoke, AccessControlAdminPermissions.RolesManage),
                requireTenant: false,
                token => dispatcher.SendAsync(new RevokeRolePermissionCommand(roleName, permissionCode), token),
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

        roles.MapGet("/{roleName}/assignments", async (
            string roleName,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(AccessControlAdminOperationNames.RoleAssignmentsList, AccessControlAdminPermissions.RolesRead),
                requireTenant: false,
                token => ListRoleAssignmentsAsync(dispatcher, roleName, token),
                cancellationToken,
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));

        roles.MapDelete("/{roleName}/assignments", async (
            string roleName,
            string subjectKind,
            string subjectId,
            string? scope,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(AccessControlAdminOperationNames.RolesUnassign, AccessControlAdminPermissions.RolesManage),
                requireTenant: false,
                token => SendUnassignRoleCommandAsync(dispatcher, subjectKind, subjectId, roleName, scope, token),
                cancellationToken,
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));
    }

    public sealed record CreateAccessRoleRequest([property: JsonPropertyName("name")] string RoleName);
    public sealed record GrantAccessRolePermissionRequest([property: JsonPropertyName("permission")] string PermissionCode);
    public sealed record AssignAccessRoleRequest(
        string? ActorId,
        [property: JsonPropertyName("scope")] string? AccessScope,
        string? SubjectKind = null,
        string? SubjectId = null);

    public sealed record AccessRoleAssignmentApiResponse(
        Guid Id,
        string SubjectKind,
        string SubjectId,
        string RoleName,
        [property: JsonConverter(typeof(AccessScopeJsonConverter))] AccessScope Scope,
        DateTimeOffset CreatedAtUtc);

    private static Task<Result<Unit>> SendAssignRoleCommandAsync(
        IRequestDispatcher dispatcher,
        AssignAccessRoleRequest request,
        string roleName,
        CancellationToken cancellationToken)
    {
        if (!TryResolveAssignmentSubject(request, out AccessSubject? subject))
        {
            return Task.FromResult(Result.Failure<Unit>(AccessControlApplicationErrors.SubjectInvalid));
        }

        if (!TryParseScope(request.AccessScope, out AccessScope? scope))
        {
            return Task.FromResult(Result.Failure<Unit>(AccessControlApplicationErrors.ScopeInvalid));
        }

        return dispatcher.SendAsync(
            new AssignRoleCommand(subject.Kind, subject.Id, roleName, scope),
            cancellationToken);
    }

    private static bool TryResolveAssignmentSubject(
        AssignAccessRoleRequest request,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AccessSubject? subject)
    {
        subject = null;
        if (string.IsNullOrWhiteSpace(request.ActorId))
        {
            return AccessSubjectKindNames.TryCreate(request.SubjectKind, request.SubjectId, out subject);
        }

        if (!string.IsNullOrWhiteSpace(request.SubjectId) &&
            !string.Equals(request.ActorId.Trim(), request.SubjectId.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.SubjectKind) &&
            (!AccessSubjectKindNames.TryParse(request.SubjectKind, out AccessSubjectKind subjectKind) ||
             subjectKind != AccessSubjectKind.AdminActor))
        {
            return false;
        }

        return AccessSubject.TryCreate(AccessSubjectKind.AdminActor, request.ActorId, out subject);
    }

    private static Task<Result<Unit>> SendUnassignRoleCommandAsync(
        IRequestDispatcher dispatcher,
        string subjectKind,
        string subjectId,
        string roleName,
        string? accessScope,
        CancellationToken cancellationToken)
    {
        if (!AccessSubjectKindNames.TryParse(subjectKind, out AccessSubjectKind parsedSubjectKind))
        {
            return Task.FromResult(Result.Failure<Unit>(AccessControlApplicationErrors.SubjectInvalid));
        }

        if (!TryParseScope(accessScope, out AccessScope? scope))
        {
            return Task.FromResult(Result.Failure<Unit>(AccessControlApplicationErrors.ScopeInvalid));
        }

        return dispatcher.SendAsync(
            new UnassignRoleCommand(parsedSubjectKind, subjectId, roleName, scope),
            cancellationToken);
    }

    private static async Task<Result<IReadOnlyList<AccessRoleAssignmentApiResponse>>> ListRoleAssignmentsAsync(
        IRequestDispatcher dispatcher,
        string roleName,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AccessControlRoleAssignmentDetails>> result = await dispatcher
            .QueryAsync(new ListRoleAssignmentsQuery(roleName), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<IReadOnlyList<AccessRoleAssignmentApiResponse>>(result.Error)
            : Result.Success<IReadOnlyList<AccessRoleAssignmentApiResponse>>(result.Value
                .Select(assignment => new AccessRoleAssignmentApiResponse(
                    assignment.Id,
                    AccessSubjectKindNames.GetName(assignment.SubjectKind),
                    assignment.SubjectId,
                    assignment.RoleName,
                    assignment.AccessScope,
                    assignment.CreatedAtUtc))
                .ToArray());
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
        new(AccessControlApplicationErrors.AssignmentAlreadyExists.Code, StatusCodes.Status409Conflict),
        new(AccessControlApplicationErrors.PermissionNotGranted.Code, StatusCodes.Status404NotFound),
        new(AccessControlApplicationErrors.AssignmentNotFound.Code, StatusCodes.Status404NotFound),
        new(AccessControlApplicationErrors.LastOwnerProtected.Code, StatusCodes.Status409Conflict));
}
