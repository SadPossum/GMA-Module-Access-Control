namespace Gma.Modules.AccessControl.Persistence.Repositories;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Framework.Runtime.Identity;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

internal sealed class AccessControlRbacRepository(AccessControlDbContext dbContext, IIdGenerator idGenerator)
    : IAccessControlRbacRepository
{
    private static readonly AccessScopeMatchOptions ExactScopeMatchOptions = new();
    private static readonly AccessScopeMatchOptions OwnerWildcardScopeMatchOptions = new(
        AllowAncestorScopeGrants: true,
        AllowGlobalScopeGrant: true);

    public Task<bool> HasAnyAssignmentsAsync(CancellationToken cancellationToken) =>
        dbContext.SubjectRoleAssignments.AnyAsync(cancellationToken);

    public Task<bool> RoleExistsAsync(string roleName, CancellationToken cancellationToken)
    {
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        return dbContext.Roles.AnyAsync(role => role.Name == normalizedRoleName, cancellationToken);
    }

    public async Task<bool> RoleHasPermissionAsync(
        string roleName,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        string normalizedPermission = AccessControlPermissionGrant.Normalize(permissionCode);

        return await dbContext.RolePermissions
            .AnyAsync(permission =>
                permission.PermissionCode == normalizedPermission &&
                permission.Role != null &&
                permission.Role.Name == normalizedRoleName,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> AssignmentExistsAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);

        int subjectKind = ToPersistedKind(subject);
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        Guid[] localRoleIds = dbContext.Roles
            .Local
            .Where(role => role.Name == normalizedRoleName)
            .Select(role => role.Id)
            .ToArray();

        if (localRoleIds.Length > 0 &&
            dbContext.SubjectRoleAssignments.Local.Any(assignment =>
                assignment.SubjectKind == subjectKind &&
                assignment.SubjectId == subject.Id &&
                localRoleIds.Contains(assignment.RoleId) &&
                assignment.ScopeValue == scope.Value))
        {
            return true;
        }

        return await dbContext.SubjectRoleAssignments
            .AnyAsync(assignment =>
                assignment.SubjectKind == subjectKind &&
                assignment.SubjectId == subject.Id &&
                assignment.ScopeValue == scope.Value &&
                assignment.Role != null &&
                assignment.Role.Name == normalizedRoleName,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> HasPermissionAsync(
        AccessSubject subject,
        PermissionCode permission,
        AccessScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(scope);

        string[] candidateScopeValues = GetCandidateScopeValues(scope);
        PersistedPermissionGrant[] candidateGrants = await this.QuerySubjectPermissionGrants(
                subject,
                permission,
                candidateScopeValues)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var grant in candidateGrants)
        {
            if (grant.PermissionCode == AccessControlPermissionGrant.OwnerWildcard &&
                AccessScopeMatcher.GrantSatisfiesRequest(grant.Scope, scope, OwnerWildcardScopeMatchOptions))
            {
                return true;
            }

            if (grant.PermissionCode == permission.Value &&
                AccessScopeMatcher.GrantSatisfiesRequest(grant.Scope, scope, ExactScopeMatchOptions))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<IReadOnlyList<AccessGrantScope>> ListGrantedScopesAsync(
        AccessSubject subject,
        PermissionCode permission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(permission);

        PersistedPermissionGrant[] grants = await this.QuerySubjectPermissionGrants(
                subject,
                permission,
                candidateScopeValues: null)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return grants
            .Select(ToAccessGrantScope)
            .Distinct()
            .OrderBy(grant => grant.Scope.Value, StringComparer.Ordinal)
            .ThenBy(grant => grant.MatchOptions.AllowGlobalScopeGrant)
            .ThenBy(grant => grant.MatchOptions.AllowAncestorScopeGrants)
            .ToArray();
    }

    public async Task EnsureSubjectAsync(
        AccessSubject subject,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        int subjectKind = ToPersistedKind(subject);
        if (await dbContext.Principals.AnyAsync(
                principal => principal.Kind == subjectKind && principal.SubjectId == subject.Id,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        dbContext.Principals.Add(new AccessPrincipal(subject, createdAtUtc));
    }

    public async Task EnsureRoleAsync(
        string roleName,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        if (await dbContext.Roles.AnyAsync(role => role.Name == normalizedRoleName, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        dbContext.Roles.Add(new AccessRole(idGenerator.NewId(), normalizedRoleName, createdAtUtc));
    }

    public async Task EnsureRolePermissionAsync(
        string roleName,
        string permissionCode,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        if (await this.RoleHasPermissionAsync(roleName, permissionCode, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await this.GrantRolePermissionAsync(roleName, permissionCode, createdAtUtc, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureRoleAssignmentAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        if (await this.AssignmentExistsAsync(subject, roleName, scope, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await this.AssignRoleAsync(subject, roleName, scope, createdAtUtc, cancellationToken).ConfigureAwait(false);
    }

    public Task<AccessControlRoleDetails> CreateRoleAsync(
        string roleName,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        AccessRole role = new(idGenerator.NewId(), roleName, createdAtUtc);
        dbContext.Roles.Add(role);

        return Task.FromResult(new AccessControlRoleDetails(role.Id, role.Name, [], 0));
    }

    public async Task GrantRolePermissionAsync(
        string roleName,
        string permissionCode,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        AccessRole role = await this.GetRoleAsync(roleName, cancellationToken).ConfigureAwait(false);
        dbContext.RolePermissions.Add(new AccessRolePermission(
            idGenerator.NewId(),
            role.Id,
            AccessControlPermissionGrant.Normalize(permissionCode),
            createdAtUtc));
    }

    public async Task AssignRoleAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);

        AccessRole role = await this.GetRoleAsync(roleName, cancellationToken).ConfigureAwait(false);
        dbContext.SubjectRoleAssignments.Add(new AccessSubjectRoleAssignment(
            idGenerator.NewId(),
            subject,
            role.Id,
            scope,
            createdAtUtc));
    }

    public async Task<IReadOnlyList<AccessControlRoleDetails>> ListRolesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Roles
            .AsNoTracking()
            .Include(role => role.Permissions)
            .Include(role => role.Assignments)
            .OrderBy(role => role.Name)
            .Select(role => new AccessControlRoleDetails(
                role.Id,
                role.Name,
                role.Permissions
                    .OrderBy(permission => permission.PermissionCode)
                    .Select(permission => permission.PermissionCode)
                    .ToArray(),
                role.Assignments.Count))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AccessRole> GetRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        AccessRole? localRole = dbContext.Roles.Local.SingleOrDefault(role => role.Name == normalizedRoleName);
        if (localRole is not null)
        {
            return localRole;
        }

        return await dbContext.Roles
            .SingleAsync(role => role.Name == normalizedRoleName, cancellationToken)
            .ConfigureAwait(false);
    }

    private IQueryable<PersistedPermissionGrant> QuerySubjectPermissionGrants(
        AccessSubject subject,
        PermissionCode permission,
        string[]? candidateScopeValues)
    {
        int subjectKind = ToPersistedKind(subject);
        string[] candidatePermissionCodes = [permission.Value, AccessControlPermissionGrant.OwnerWildcard];

        IQueryable<AccessSubjectRoleAssignment> assignments = dbContext.SubjectRoleAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.SubjectKind == subjectKind &&
                assignment.SubjectId == subject.Id);

        if (candidateScopeValues is not null)
        {
            assignments = assignments.Where(assignment => candidateScopeValues.Contains(assignment.ScopeValue));
        }

        IQueryable<AccessRolePermission> permissionGrants = dbContext.RolePermissions
            .AsNoTracking()
            .Where(permissionGrant => candidatePermissionCodes.Contains(permissionGrant.PermissionCode));

        return assignments
            .Join(
                permissionGrants,
                assignment => assignment.RoleId,
                permissionGrant => permissionGrant.RoleId,
                (assignment, permissionGrant) => new PersistedPermissionGrant(
                    assignment.SubjectKind,
                    assignment.SubjectId,
                    assignment.ScopeValue,
                    permissionGrant.PermissionCode));
    }

    private static AccessGrantScope ToAccessGrantScope(PersistedPermissionGrant grant) =>
        grant.PermissionCode == AccessControlPermissionGrant.OwnerWildcard
            ? new AccessGrantScope(grant.Scope, OwnerWildcardScopeMatchOptions)
            : new AccessGrantScope(grant.Scope, ExactScopeMatchOptions);

    private static string[] GetCandidateScopeValues(AccessScope requestedScope)
    {
        if (requestedScope.IsGlobal)
        {
            return [AccessScope.Global.Value];
        }

        List<AccessScope> scopes = [requestedScope, AccessScope.Global];
        for (int segmentCount = 1; segmentCount < requestedScope.Segments.Count; segmentCount++)
        {
            scopes.Add(AccessScope.Create(requestedScope.Segments.Take(segmentCount)));
        }

        return scopes
            .Distinct()
            .Select(scope => scope.Value)
            .ToArray();
    }

    private static int ToPersistedKind(AccessSubject subject)
    {
        if (subject.Kind == AccessSubjectKind.Unknown || !Enum.IsDefined(subject.Kind))
        {
            throw new ArgumentException("Access subject kind must be a defined non-unknown value.", nameof(subject));
        }

        return (int)subject.Kind;
    }

    private sealed record PersistedPermissionGrant(
        int SubjectKind,
        string SubjectId,
        string ScopeValue,
        string PermissionCode)
    {
        public AccessScope Scope => AccessScope.Parse(this.ScopeValue);
    }
}
