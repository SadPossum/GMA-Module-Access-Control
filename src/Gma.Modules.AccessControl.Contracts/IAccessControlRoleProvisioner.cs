namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public interface IAccessControlRoleProvisioner
{
    Task EnsureRoleAsync(
        AccessControlRoleDefinition role,
        CancellationToken cancellationToken = default);

    Task EnsureAssignmentAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        CancellationToken cancellationToken = default);

    Task<AccessControlAssignmentRemovalOutcome> RemoveAssignmentAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        CancellationToken cancellationToken = default);

    Task<bool> HasAssignmentAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        CancellationToken cancellationToken = default);

    async Task<bool> HasAnyAssignmentAsync(
        AccessSubject subject,
        IReadOnlyCollection<string> roleNames,
        AccessScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(roleNames);
        ArgumentNullException.ThrowIfNull(scope);

        foreach (string roleName in roleNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await this.HasAssignmentAsync(subject, roleName, scope, cancellationToken)
                .ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    Task<AccessControlPage<AccessControlRoleAssignment>> ListAssignmentsAsync(
        string roleName,
        AccessScope scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
