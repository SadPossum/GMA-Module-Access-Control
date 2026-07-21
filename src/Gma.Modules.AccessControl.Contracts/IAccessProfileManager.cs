namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;
using Gma.Framework.Results;

public interface IAccessProfileManager
{
    Task<Result<AccessControlPage<AccessProfileDto>>> ListProfilesAsync(
        AccessScope ownerScope,
        bool includeArchived,
        int page,
        int pageSize,
        AccessSubject actor,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<string>>> ListAllowedPermissionsAsync(
        AccessScope ownerScope,
        AccessSubject actor,
        CancellationToken cancellationToken = default);

    Task<Result<AccessProfileDto>> GetProfileAsync(
        Guid profileId,
        AccessScope ownerScope,
        AccessSubject actor,
        CancellationToken cancellationToken = default);

    Task<Result<AccessProfileDto>> CreateProfileAsync(
        AccessScope ownerScope,
        AccessProfileDefinition definition,
        AccessSubject actor,
        CancellationToken cancellationToken = default);

    Task<Result<AccessProfileDto>> UpdateProfileAsync(
        Guid profileId,
        AccessScope ownerScope,
        AccessProfileUpdate update,
        AccessSubject actor,
        CancellationToken cancellationToken = default);

    Task<Result> ArchiveProfileAsync(
        Guid profileId,
        AccessScope ownerScope,
        long expectedVersion,
        AccessSubject actor,
        CancellationToken cancellationToken = default);
}
