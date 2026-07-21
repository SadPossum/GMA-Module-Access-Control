namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Permissions;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Queries;
using Gma.Modules.AccessControl.Contracts;

internal sealed class AccessProfileManager(
    IRequestDispatcher dispatcher,
    IAccessAuthorizationService authorization) : IAccessProfileManager
{
    public async Task<Result<AccessControlPage<AccessProfileDto>>> ListProfilesAsync(
        AccessScope ownerScope,
        bool includeArchived,
        int page,
        int pageSize,
        AccessSubject actor,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(ownerScope);
        ArgumentNullException.ThrowIfNull(actor);
        Result access = await this.AuthorizeAsync(
            actor,
            ownerScope,
            AccessControlProfilePermissionCodes.Read,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return Result.Failure<AccessControlPage<AccessProfileDto>>(access.Error);
        }

        Result<AccessControlPage<AccessProfileDetails>> result = await dispatcher.QueryAsync(
                new ListAccessProfilesQuery(ownerScope, includeArchived, page, pageSize),
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsFailure
            ? Result.Failure<AccessControlPage<AccessProfileDto>>(result.Error)
            : Result.Success(AccessProfileContractMappings.ToContract(result.Value));
    }

    public async Task<Result<IReadOnlyList<string>>> ListAllowedPermissionsAsync(
        AccessScope ownerScope,
        AccessSubject actor,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(ownerScope);
        ArgumentNullException.ThrowIfNull(actor);
        Result access = await this.AuthorizeAsync(
            actor,
            ownerScope,
            AccessControlProfilePermissionCodes.Read,
            cancellationToken).ConfigureAwait(false);
        return access.IsFailure
            ? Result.Failure<IReadOnlyList<string>>(access.Error)
            : await dispatcher.QueryAsync(
                new ListAllowedAccessProfilePermissionsQuery(),
                cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AccessProfileDto>> GetProfileAsync(
        Guid profileId,
        AccessScope ownerScope,
        AccessSubject actor,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);
        ValidateScope(ownerScope);
        ArgumentNullException.ThrowIfNull(actor);
        Result access = await this.AuthorizeAsync(
            actor,
            ownerScope,
            AccessControlProfilePermissionCodes.Read,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return Result.Failure<AccessProfileDto>(access.Error);
        }

        Result<AccessProfileDetails> result = await dispatcher.QueryAsync(
                new GetAccessProfileQuery(profileId, ownerScope),
                cancellationToken)
            .ConfigureAwait(false);
        return Map(result);
    }

    public async Task<Result<AccessProfileDto>> CreateProfileAsync(
        AccessScope ownerScope,
        AccessProfileDefinition definition,
        AccessSubject actor,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(ownerScope);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(actor);
        Result access = await this.AuthorizeAsync(
            actor,
            ownerScope,
            AccessControlProfilePermissionCodes.Manage,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return Result.Failure<AccessProfileDto>(access.Error);
        }

        Result<AccessProfileDetails> result = await dispatcher.SendAsync(
                new CreateAccessProfileCommand(
                    ownerScope,
                    definition.Key,
                    definition.DisplayName,
                    definition.Description,
                    definition.Permissions,
                    actor),
                cancellationToken)
            .ConfigureAwait(false);
        return Map(result);
    }

    public async Task<Result<AccessProfileDto>> UpdateProfileAsync(
        Guid profileId,
        AccessScope ownerScope,
        AccessProfileUpdate update,
        AccessSubject actor,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);
        ValidateScope(ownerScope);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(actor);
        Result access = await this.AuthorizeAsync(
            actor,
            ownerScope,
            AccessControlProfilePermissionCodes.Manage,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return Result.Failure<AccessProfileDto>(access.Error);
        }

        Result<AccessProfileDetails> result = await dispatcher.SendAsync(
                new UpdateAccessProfileCommand(
                    profileId,
                    ownerScope,
                    update.DisplayName,
                    update.Description,
                    update.Permissions,
                    update.ExpectedVersion,
                    actor),
                cancellationToken)
            .ConfigureAwait(false);
        return Map(result);
    }

    public async Task<Result> ArchiveProfileAsync(
        Guid profileId,
        AccessScope ownerScope,
        long expectedVersion,
        AccessSubject actor,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);
        ValidateScope(ownerScope);
        ArgumentNullException.ThrowIfNull(actor);
        Result access = await this.AuthorizeAsync(
            actor,
            ownerScope,
            AccessControlProfilePermissionCodes.Manage,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return access;
        }

        Result<Unit> result = await dispatcher.SendAsync(
                new ArchiveAccessProfileCommand(profileId, ownerScope, expectedVersion, actor),
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsFailure ? Result.Failure(result.Error) : Result.Success();
    }

    private static Result<AccessProfileDto> Map(Result<AccessProfileDetails> result) =>
        result.IsFailure
            ? Result.Failure<AccessProfileDto>(result.Error)
            : Result.Success(AccessProfileContractMappings.ToContract(result.Value));

    private async Task<Result> AuthorizeAsync(
        AccessSubject actor,
        AccessScope ownerScope,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        AccessDecision decision = await authorization.AuthorizeAsync(
            new AccessRequirement(actor, PermissionCode.Create(permissionCode), ownerScope),
            cancellationToken).ConfigureAwait(false);
        return decision.IsAllowed
            ? Result.Success()
            : Result.Failure(AccessProfileManagementErrors.AccessDenied);
    }

    private static void ValidateProfileId(Guid profileId)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty access-profile id is required.", nameof(profileId));
        }
    }

    private static void ValidateScope(AccessScope ownerScope)
    {
        ArgumentNullException.ThrowIfNull(ownerScope);
        if (ownerScope.IsGlobal)
        {
            throw new ArgumentException("A non-global access-profile owner scope is required.", nameof(ownerScope));
        }
    }
}
