namespace Gma.Modules.AccessControl.Api;

using Gma.Framework.AccessControl;
using Gma.Framework.AccessControl.AspNetCore;
using Gma.Framework.Api.Modules;
using Gma.Framework.Api.Observability;
using Gma.Framework.Api.Results;
using Gma.Framework.Cqrs;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Pagination;
using Gma.Framework.Results;
using Gma.Framework.Security;
using Gma.Framework.Security.AspNetCore;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Queries;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

public sealed class AccessControlApiModule : IModule
{
    public string Name => AccessControlModuleMetadata.Name;

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.SelectModuleProfile(AccessControlProfiles.Default, "Gma.Modules.AccessControl.Api");
        builder.Services.AddOptions<AccessControlApiSecurityOptions>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IAccessHttpScopeResolver, AccessProfileScopeResolver>());
        builder.Services.AddAccessControlApplication(builder.Configuration);
        builder.AddAccessControlPersistence();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AuthenticationAssuranceRequirement? profileManagementAssurance = endpoints.ServiceProvider
            .GetRequiredService<IOptions<AccessControlApiSecurityOptions>>()
            .Value
            .ProfileManagementAssurance;
        RouteGroupBuilder profiles = endpoints.MapGroup("/api/access-control/profiles")
            .WithModuleName(this.Name)
            .WithTags("AccessControl Profiles")
            .RequireAuthorization();

        profiles.MapGet("/", async (
            string scope,
            bool? includeArchived,
            int? page,
            int? pageSize,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            (await ListProfilesAsync(
                dispatcher, scope, includeArchived ?? false,
                page ?? PageRequest.DefaultPage, pageSize ?? PageRequest.DefaultPageSize,
                cancellationToken).ConfigureAwait(false)).ToHttpResult(ErrorStatusCodes))
            .Produces<AccessControlPage<AccessProfileDto>>(StatusCodes.Status200OK)
            .RequireResolvedScopePermission(AccessControlProfilePermissionCodes.Read, AccessProfileScopeResolver.ResolverName);

        profiles.MapGet("/permissions", async (
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            (await dispatcher.QueryAsync(new ListAllowedAccessProfilePermissionsQuery(), cancellationToken)
                .ConfigureAwait(false)).ToHttpResult(ErrorStatusCodes))
            .Produces<string[]>(StatusCodes.Status200OK)
            .RequireResolvedScopePermission(AccessControlProfilePermissionCodes.Read, AccessProfileScopeResolver.ResolverName);

        profiles.MapGet("/{profileId:guid}", async (
            Guid profileId,
            string scope,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            (await GetProfileAsync(dispatcher, profileId, scope, cancellationToken).ConfigureAwait(false))
                .ToHttpResult(ErrorStatusCodes))
            .Produces<AccessProfileDto>(StatusCodes.Status200OK)
            .RequireResolvedScopePermission(AccessControlProfilePermissionCodes.Read, AccessProfileScopeResolver.ResolverName);

        RouteHandlerBuilder createProfile = profiles.MapPost("/", async (
            string scope,
            CreateAccessProfileRequest request,
            HttpContext httpContext,
            IAccessHttpSubjectResolver subjects,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            (await CreateProfileAsync(
                dispatcher, scope, request, ResolveActor(httpContext, subjects), cancellationToken)
                .ConfigureAwait(false)).ToHttpResult(ErrorStatusCodes))
            .Produces<AccessProfileDto>(StatusCodes.Status200OK)
            .RequireResolvedScopePermission(AccessControlProfilePermissionCodes.Manage, AccessProfileScopeResolver.ResolverName);
        RequireAssuranceWhenConfigured(createProfile, profileManagementAssurance);

        RouteHandlerBuilder updateProfile = profiles.MapPut("/{profileId:guid}", async (
            Guid profileId,
            string scope,
            UpdateAccessProfileRequest request,
            HttpContext httpContext,
            IAccessHttpSubjectResolver subjects,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            (await UpdateProfileAsync(
                dispatcher, profileId, scope, request, ResolveActor(httpContext, subjects), cancellationToken)
                .ConfigureAwait(false)).ToHttpResult(ErrorStatusCodes))
            .Produces<AccessProfileDto>(StatusCodes.Status200OK)
            .RequireResolvedScopePermission(AccessControlProfilePermissionCodes.Manage, AccessProfileScopeResolver.ResolverName);
        RequireAssuranceWhenConfigured(updateProfile, profileManagementAssurance);

        RouteHandlerBuilder archiveProfile = profiles.MapPost("/{profileId:guid}/archive", async (
            Guid profileId,
            string scope,
            ArchiveAccessProfileRequest request,
            HttpContext httpContext,
            IAccessHttpSubjectResolver subjects,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            (await ArchiveProfileAsync(
                dispatcher, profileId, scope, request, ResolveActor(httpContext, subjects), cancellationToken)
            .ConfigureAwait(false)).ToHttpResult(ErrorStatusCodes))
            .RequireResolvedScopePermission(AccessControlProfilePermissionCodes.Manage, AccessProfileScopeResolver.ResolverName);
        RequireAssuranceWhenConfigured(archiveProfile, profileManagementAssurance);

        profiles.MapGet("/{profileId:guid}/assignments", async (
            Guid profileId,
            string scope,
            int? page,
            int? pageSize,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            (await ListAssignmentsAsync(
                dispatcher, profileId, scope,
                page ?? PageRequest.DefaultPage, pageSize ?? PageRequest.DefaultPageSize,
                cancellationToken).ConfigureAwait(false)).ToHttpResult(ErrorStatusCodes))
            .Produces<AccessControlPage<AccessProfileAssignmentDto>>(StatusCodes.Status200OK)
            .RequireResolvedScopePermission(AccessControlProfilePermissionCodes.Read, AccessProfileScopeResolver.ResolverName);

        RouteHandlerBuilder assignProfile = profiles.MapPost("/{profileId:guid}/assignments", async (
            Guid profileId,
            string scope,
            AccessProfileAssignmentRequest request,
            HttpContext httpContext,
            IAccessHttpSubjectResolver subjects,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            (await AssignProfileAsync(
                dispatcher, profileId, scope, request, ResolveActor(httpContext, subjects), cancellationToken)
                .ConfigureAwait(false)).ToHttpResult(ErrorStatusCodes))
            .Produces<AccessProfileAssignmentDto>(StatusCodes.Status200OK)
            .RequireResolvedScopePermission(AccessControlProfilePermissionCodes.Assign, AccessProfileScopeResolver.ResolverName);
        RequireAssuranceWhenConfigured(assignProfile, profileManagementAssurance);

        RouteHandlerBuilder unassignProfile = profiles.MapDelete("/{profileId:guid}/assignments", async (
            Guid profileId,
            string scope,
            string subjectKind,
            string subjectId,
            string? assignmentScope,
            HttpContext httpContext,
            IAccessHttpSubjectResolver subjects,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            (await UnassignProfileAsync(
                dispatcher, profileId, scope, subjectKind, subjectId, assignmentScope,
                ResolveActor(httpContext, subjects), cancellationToken).ConfigureAwait(false))
                .ToHttpResult(ErrorStatusCodes))
            .RequireResolvedScopePermission(AccessControlProfilePermissionCodes.Assign, AccessProfileScopeResolver.ResolverName);
        RequireAssuranceWhenConfigured(unassignProfile, profileManagementAssurance);

        profiles.MapGet("/{profileId:guid}/history", async (
            Guid profileId,
            string scope,
            int? page,
            int? pageSize,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            (await ListHistoryAsync(
                dispatcher, profileId, scope,
                page ?? PageRequest.DefaultPage, pageSize ?? PageRequest.DefaultPageSize,
                cancellationToken).ConfigureAwait(false)).ToHttpResult(ErrorStatusCodes))
            .Produces<AccessControlPage<AccessProfileChangeDto>>(StatusCodes.Status200OK)
            .RequireResolvedScopePermission(AccessControlProfilePermissionCodes.Read, AccessProfileScopeResolver.ResolverName);
    }

    private static RouteHandlerBuilder RequireAssuranceWhenConfigured(
        RouteHandlerBuilder endpoint,
        AuthenticationAssuranceRequirement? requirement) =>
        requirement is null
            ? endpoint
            : endpoint.RequireAuthenticationAssurance(requirement);

    public sealed record CreateAccessProfileRequest(
        string Key,
        string DisplayName,
        string? Description,
        IReadOnlyCollection<string> Permissions);

    public sealed record UpdateAccessProfileRequest(
        string DisplayName,
        string? Description,
        IReadOnlyCollection<string> Permissions,
        long ExpectedVersion);

    public sealed record ArchiveAccessProfileRequest(long ExpectedVersion);
    public sealed record AccessProfileAssignmentRequest(
        string SubjectKind,
        string SubjectId,
        string? AssignmentScope = null);

    private static Task<Result<AccessControlPage<AccessProfileDto>>> ListProfilesAsync(
        IRequestDispatcher dispatcher,
        string scope,
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        TryScope(scope, out AccessScope? ownerScope)
            ? MapAsync(
                dispatcher.QueryAsync(
                    new ListAccessProfilesQuery(ownerScope, includeArchived, page, pageSize),
                    cancellationToken),
                source => AccessProfileApiMappings.ToPage(source, AccessProfileApiMappings.ToDto))
            : InvalidScope<AccessControlPage<AccessProfileDto>>();

    private static Task<Result<AccessProfileDto>> GetProfileAsync(
        IRequestDispatcher dispatcher,
        Guid profileId,
        string scope,
        CancellationToken cancellationToken) =>
        TryScope(scope, out AccessScope? ownerScope)
            ? MapAsync(
                dispatcher.QueryAsync(new GetAccessProfileQuery(profileId, ownerScope), cancellationToken),
                AccessProfileApiMappings.ToDto)
            : InvalidScope<AccessProfileDto>();

    private static Task<Result<AccessProfileDto>> CreateProfileAsync(
        IRequestDispatcher dispatcher,
        string scope,
        CreateAccessProfileRequest request,
        AccessSubject? actor,
        CancellationToken cancellationToken) =>
        TryScopeAndActor(scope, actor, out AccessScope? ownerScope, out AccessSubject? resolvedActor)
            ? MapAsync(
                dispatcher.SendAsync(new CreateAccessProfileCommand(
                    ownerScope, request.Key, request.DisplayName, request.Description,
                    request.Permissions ?? [], resolvedActor), cancellationToken),
                AccessProfileApiMappings.ToDto)
            : InvalidScopeOrActor<AccessProfileDto>();

    private static Task<Result<AccessProfileDto>> UpdateProfileAsync(
        IRequestDispatcher dispatcher,
        Guid profileId,
        string scope,
        UpdateAccessProfileRequest request,
        AccessSubject? actor,
        CancellationToken cancellationToken) =>
        TryScopeAndActor(scope, actor, out AccessScope? ownerScope, out AccessSubject? resolvedActor)
            ? MapAsync(
                dispatcher.SendAsync(new UpdateAccessProfileCommand(
                    profileId, ownerScope, request.DisplayName, request.Description,
                    request.Permissions ?? [], request.ExpectedVersion, resolvedActor), cancellationToken),
                AccessProfileApiMappings.ToDto)
            : InvalidScopeOrActor<AccessProfileDto>();

    private static Task<Result<Unit>> ArchiveProfileAsync(
        IRequestDispatcher dispatcher,
        Guid profileId,
        string scope,
        ArchiveAccessProfileRequest request,
        AccessSubject? actor,
        CancellationToken cancellationToken) =>
        TryScopeAndActor(scope, actor, out AccessScope? ownerScope, out AccessSubject? resolvedActor)
            ? dispatcher.SendAsync(new ArchiveAccessProfileCommand(
                profileId, ownerScope, request.ExpectedVersion, resolvedActor), cancellationToken)
            : InvalidScopeOrActor<Unit>();

    private static Task<Result<AccessControlPage<AccessProfileAssignmentDto>>> ListAssignmentsAsync(
        IRequestDispatcher dispatcher,
        Guid profileId,
        string scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        TryScope(scope, out AccessScope? ownerScope)
            ? MapAsync(
                dispatcher.QueryAsync(new ListAccessProfileAssignmentsQuery(
                    profileId, ownerScope, page, pageSize), cancellationToken),
                source => AccessProfileApiMappings.ToPage(source, AccessProfileApiMappings.ToDto))
            : InvalidScope<AccessControlPage<AccessProfileAssignmentDto>>();

    private static Task<Result<AccessProfileAssignmentDto>> AssignProfileAsync(
        IRequestDispatcher dispatcher,
        Guid profileId,
        string scope,
        AccessProfileAssignmentRequest request,
        AccessSubject? actor,
        CancellationToken cancellationToken) =>
        TryScopeAndActor(scope, actor, out AccessScope? ownerScope, out AccessSubject? resolvedActor) &&
        TryAssignmentScope(request.AssignmentScope, ownerScope, out AccessScope? assignmentScope) &&
        AccessSubjectKindNames.TryCreate(request.SubjectKind, request.SubjectId, out AccessSubject? subject)
            ? MapAsync(
                dispatcher.SendAsync(new AssignAccessProfileCommand(
                    profileId, ownerScope, assignmentScope, subject, resolvedActor), cancellationToken),
                AccessProfileApiMappings.ToDto)
            : InvalidScopeOrActor<AccessProfileAssignmentDto>();

    private static Task<Result<Unit>> UnassignProfileAsync(
        IRequestDispatcher dispatcher,
        Guid profileId,
        string scope,
        string subjectKind,
        string subjectId,
        string? assignmentScopeValue,
        AccessSubject? actor,
        CancellationToken cancellationToken) =>
        TryScopeAndActor(scope, actor, out AccessScope? ownerScope, out AccessSubject? resolvedActor) &&
        TryAssignmentScope(assignmentScopeValue, ownerScope, out AccessScope? assignmentScope) &&
        AccessSubjectKindNames.TryCreate(subjectKind, subjectId, out AccessSubject? subject)
            ? dispatcher.SendAsync(new UnassignAccessProfileCommand(
                profileId, ownerScope, assignmentScope, subject, resolvedActor), cancellationToken)
            : InvalidScopeOrActor<Unit>();

    private static Task<Result<AccessControlPage<AccessProfileChangeDto>>> ListHistoryAsync(
        IRequestDispatcher dispatcher,
        Guid profileId,
        string scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        TryScope(scope, out AccessScope? ownerScope)
            ? MapAsync(
                dispatcher.QueryAsync(new ListAccessProfileChangesQuery(
                    profileId, ownerScope, page, pageSize), cancellationToken),
                source => AccessProfileApiMappings.ToPage(source, AccessProfileApiMappings.ToDto))
            : InvalidScope<AccessControlPage<AccessProfileChangeDto>>();

    private static AccessSubject? ResolveActor(HttpContext context, IAccessHttpSubjectResolver subjects) =>
        subjects.ResolveSubject(context);

    private static bool TryScope(string value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AccessScope? scope) =>
        AccessScope.TryParse(value, out scope) && !scope.IsGlobal;

    private static bool TryAssignmentScope(
        string? value,
        AccessScope ownerScope,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AccessScope? assignmentScope)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            assignmentScope = ownerScope;
            return true;
        }

        return TryScope(value, out assignmentScope);
    }

    private static bool TryScopeAndActor(
        string value,
        AccessSubject? actor,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AccessScope? scope,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AccessSubject? resolvedActor)
    {
        scope = null;
        resolvedActor = actor;
        return actor is not null && TryScope(value, out scope);
    }

    private static Task<Result<T>> InvalidScope<T>() =>
        Task.FromResult(Result.Failure<T>(AccessControlApplicationErrors.ProfileScopeRequired));

    private static Task<Result<T>> InvalidScopeOrActor<T>() =>
        Task.FromResult(Result.Failure<T>(AccessControlApplicationErrors.SubjectInvalid));

    private static async Task<Result<TTarget>> MapAsync<TSource, TTarget>(
        Task<Result<TSource>> operation,
        Func<TSource, TTarget> map)
    {
        Result<TSource> result = await operation.ConfigureAwait(false);
        return result.IsSuccess
            ? Result.Success(map(result.Value))
            : Result.Failure<TTarget>(result.Error);
    }

    private static readonly ApiErrorStatusCodeMap ErrorStatusCodes = ApiErrorStatusCodeMap.Create(
        new(AccessControlApplicationErrors.ProfileNotFound.Code, StatusCodes.Status404NotFound),
        new(AccessControlApplicationErrors.ProfileAlreadyExists.Code, StatusCodes.Status409Conflict),
        new(AccessControlApplicationErrors.ProfileAssignmentAlreadyExists.Code, StatusCodes.Status409Conflict),
        new(AccessControlApplicationErrors.ProfileAssignmentNotFound.Code, StatusCodes.Status404NotFound),
        new(AccessControlApplicationErrors.ProfileAssignmentRejected.Code, StatusCodes.Status403Forbidden),
        new(AccessControlApplicationErrors.ProfilePermissionNotAllowed.Code, StatusCodes.Status422UnprocessableEntity),
        new(AccessControlApplicationErrors.ProfilePermissionEscalation.Code, StatusCodes.Status403Forbidden),
        new(AccessControlApplicationErrors.ProfileMutationRejected.Code, StatusCodes.Status409Conflict),
        new(AccessControlApplicationErrors.ProfileMutationAdmissionUnavailable.Code, StatusCodes.Status503ServiceUnavailable),
        new(AccessControlApplicationErrors.ProfileScopeRequired.Code, StatusCodes.Status400BadRequest),
        new(AccessControlApplicationErrors.ProfileKeyInvalid.Code, StatusCodes.Status400BadRequest),
        new(AccessControlApplicationErrors.ProfileDisplayNameInvalid.Code, StatusCodes.Status400BadRequest),
        new(AccessControlApplicationErrors.ProfileDescriptionInvalid.Code, StatusCodes.Status400BadRequest),
        new(AccessControlApplicationErrors.ProfilePermissionInvalid.Code, StatusCodes.Status400BadRequest),
        new(AccessControlApplicationErrors.ProfilePermissionLimitExceeded.Code, StatusCodes.Status400BadRequest),
        new(AccessControlApplicationErrors.ProfileVersionConflict.Code, StatusCodes.Status409Conflict),
        new(AccessControlApplicationErrors.ProfileArchived.Code, StatusCodes.Status409Conflict),
        new(AccessControlApplicationErrors.ProfileAlreadyArchived.Code, StatusCodes.Status409Conflict));
}
