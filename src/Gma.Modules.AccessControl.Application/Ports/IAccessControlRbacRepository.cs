namespace Gma.Modules.AccessControl.Application.Ports;

using Gma.Framework.AccessControl;
using Gma.Framework.Pagination;
using Gma.Framework.Permissions;
using Gma.Modules.AccessControl.Contracts;

internal interface IAccessControlRbacRepository
{
    Task<bool> HasAnyAssignmentsAsync(CancellationToken cancellationToken);
    Task<bool> TryBootstrapOwnerAsync(
        AccessSubject subject,
        string roleName,
        DateTimeOffset createdAtUtc,
        bool allowWhenAssignmentsExist,
        CancellationToken cancellationToken);
    Task<bool> RoleExistsAsync(string roleName, CancellationToken cancellationToken);
    Task<string[]?> FindRolePermissionsAsync(string roleName, CancellationToken cancellationToken);
    Task<bool> RoleHasPermissionAsync(string roleName, string permissionCode, CancellationToken cancellationToken);
    Task<bool> RoleHasActiveTemporaryAssignmentsAsync(string roleName, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<bool> AssignmentExistsAsync(AccessSubject subject, string roleName, AccessScope scope, CancellationToken cancellationToken);
    Task<bool> HasPermissionAsync(AccessSubject subject, PermissionCode permission, AccessScope scope, CancellationToken cancellationToken);
    Task<IReadOnlyList<bool>> HasPermissionsAsync(IReadOnlyList<AccessRequirement> requirements, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessGrantScope>> ListGrantedScopesAsync(AccessSubject subject, PermissionCode permission, CancellationToken cancellationToken);
    Task EnsureSubjectAsync(AccessSubject subject, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task EnsureRoleAsync(string roleName, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task EnsureRolePermissionAsync(string roleName, string permissionCode, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task EnsureRoleAssignmentAsync(AccessSubject subject, string roleName, AccessScope scope, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task EnsureRoleDefinitionAsync(string roleName, IReadOnlyCollection<string> permissions, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task EnsureRoleAssignmentProvisionedAsync(AccessSubject subject, string roleName, AccessScope scope, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task<AccessControlRoleDetails> CreateRoleAsync(string roleName, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task<AccessControlRolePermissionGrantPersistenceOutcome> GrantRolePermissionAsync(
        string roleName,
        string permissionCode,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken);
    Task AssignRoleAsync(AccessSubject subject, string roleName, AccessScope scope, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task<AccessControlRoleAssignmentPersistenceOutcome> TryAssignRoleAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        DateTimeOffset? expiresAtUtc,
        IReadOnlyCollection<string> expectedRolePermissions,
        CancellationToken cancellationToken);
    Task<AccessControlRemovalOutcome> RevokeRolePermissionAsync(string roleName, string permissionCode, CancellationToken cancellationToken);
    Task<AccessControlRemovalOutcome> UnassignRoleAsync(AccessSubject subject, string roleName, AccessScope scope, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessControlRoleDetails>> ListRolesAsync(CancellationToken cancellationToken);
    Task<AccessControlPage<AccessControlRoleDetails>> ListRolesPageAsync(PageRequest pageRequest, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessControlRoleAssignmentDetails>> ListRoleAssignmentsAsync(string roleName, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessControlRoleAssignmentDetails>> ListRoleAssignmentsAsync(string roleName, AccessScope scope, CancellationToken cancellationToken);
    Task<AccessControlPage<AccessControlRoleAssignmentDetails>> ListRoleAssignmentsPageAsync(string roleName, AccessScope scope, PageRequest pageRequest, bool includeInactive, CancellationToken cancellationToken);
    Task<AccessControlPage<AccessControlRoleAssignmentDetails>> ListRoleAssignmentsPageAsync(string roleName, PageRequest pageRequest, bool includeInactive, CancellationToken cancellationToken);
}
