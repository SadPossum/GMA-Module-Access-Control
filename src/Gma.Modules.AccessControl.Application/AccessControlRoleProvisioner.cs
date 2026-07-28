namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Framework.Pagination;
using Gma.Framework.Permissions;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Contracts;

internal sealed class AccessControlRoleProvisioner(
    IAccessControlRbacRepository repository,
    ISystemClock clock) : IAccessControlRoleProvisioner
{
    public async Task EnsureRoleAsync(
        AccessControlRoleDefinition role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (!AccessControlRoleName.TryNormalize(role.Name, out string? roleName))
        {
            throw new ArgumentException("The access-control role name is invalid.", nameof(role));
        }

        ArgumentNullException.ThrowIfNull(role.Permissions);
        string[] permissions = role.Permissions
            .Select(AccessControlPermissionGrant.Normalize)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await repository.EnsureRoleDefinitionAsync(
                roleName,
                permissions,
                clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task EnsureAssignmentAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);
        string normalizedRoleName = AccessControlRoleName.Normalize(roleName);
        await repository.EnsureRoleAssignmentProvisionedAsync(
                subject,
                normalizedRoleName,
                scope,
                clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AccessControlAssignmentRemovalOutcome> RemoveAssignmentAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);
        AccessControlRemovalOutcome outcome = await repository
            .UnassignRoleAsync(
                subject,
                AccessControlRoleName.Normalize(roleName),
                scope,
                clock.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);
        return outcome switch
        {
            AccessControlRemovalOutcome.Removed => AccessControlAssignmentRemovalOutcome.Removed,
            AccessControlRemovalOutcome.NotFound => AccessControlAssignmentRemovalOutcome.NotFound,
            AccessControlRemovalOutcome.LastOwnerProtected => AccessControlAssignmentRemovalOutcome.LastOwnerProtected,
            _ => AccessControlAssignmentRemovalOutcome.Unknown
        };
    }

    public Task<bool> HasAssignmentAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);
        return repository.AssignmentExistsAsync(
            subject,
            AccessControlRoleName.Normalize(roleName),
            scope,
            cancellationToken);
    }

    public async Task<AccessControlPage<AccessControlRoleAssignment>> ListAssignmentsAsync(
        string roleName,
        AccessScope scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        PageRequest request = PageRequest.Normalize(page, pageSize);
        AccessControlPage<AccessControlRoleAssignmentDetails> assignments = await repository
            .ListRoleAssignmentsPageAsync(
                AccessControlRoleName.Normalize(roleName),
                scope,
                request,
                includeInactive: false,
                cancellationToken)
            .ConfigureAwait(false);

        return new AccessControlPage<AccessControlRoleAssignment>(
            assignments.Items
                .Select(assignment => new AccessControlRoleAssignment(
                    assignment.Id,
                    assignment.SubjectKind,
                    assignment.SubjectId,
                    assignment.RoleName,
                    assignment.AccessScope,
                    assignment.CreatedAtUtc,
                    assignment.ExpiresAtUtc,
                    assignment.RevokedAtUtc,
                    assignment.Status))
                .ToArray(),
            assignments.Page,
            assignments.PageSize,
            assignments.HasMore);
    }
}
