namespace Gma.Modules.AccessControl.Application.Ports;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;

public interface IAccessControlRbacRepository
{
    Task<bool> HasAnyAssignmentsAsync(CancellationToken cancellationToken);
    Task<bool> TryBootstrapOwnerAsync(
        AccessSubject subject,
        string roleName,
        DateTimeOffset createdAtUtc,
        bool allowWhenAssignmentsExist,
        CancellationToken cancellationToken);
    Task<bool> RoleExistsAsync(string roleName, CancellationToken cancellationToken);
    Task<bool> RoleHasPermissionAsync(string roleName, string permissionCode, CancellationToken cancellationToken);
    Task<bool> AssignmentExistsAsync(AccessSubject subject, string roleName, AccessScope scope, CancellationToken cancellationToken);
    Task<bool> HasPermissionAsync(AccessSubject subject, PermissionCode permission, AccessScope scope, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessGrantScope>> ListGrantedScopesAsync(AccessSubject subject, PermissionCode permission, CancellationToken cancellationToken);
    Task EnsureSubjectAsync(AccessSubject subject, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task EnsureRoleAsync(string roleName, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task EnsureRolePermissionAsync(string roleName, string permissionCode, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task EnsureRoleAssignmentAsync(AccessSubject subject, string roleName, AccessScope scope, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task<AccessControlRoleDetails> CreateRoleAsync(string roleName, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task GrantRolePermissionAsync(string roleName, string permissionCode, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task AssignRoleAsync(AccessSubject subject, string roleName, AccessScope scope, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task<AccessControlRemovalOutcome> RevokeRolePermissionAsync(string roleName, string permissionCode, CancellationToken cancellationToken);
    Task<AccessControlRemovalOutcome> UnassignRoleAsync(AccessSubject subject, string roleName, AccessScope scope, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessControlRoleDetails>> ListRolesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessControlRoleAssignmentDetails>> ListRoleAssignmentsAsync(string roleName, CancellationToken cancellationToken);
}
