namespace Gma.Modules.AccessControl.Persistence.Repositories;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Framework.Pagination;
using Gma.Framework.Runtime.Identity;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Persistence.Entities;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;
using DomainAccessProfileStatus = Gma.Modules.AccessControl.Domain.Enums.AccessProfileStatus;

internal sealed class AccessControlRbacRepository(
    AccessControlDbContext dbContext,
    IIdGenerator idGenerator,
    IAccessScopeMatchOptionsResolver scopeMatchOptionsResolver)
    : IAccessControlRbacRepository
{
    private static readonly AccessScopeMatchOptions OwnerWildcardScopeMatchOptions = new(
        AllowAncestorScopeGrants: true,
        AllowGlobalScopeGrant: true);

    public Task<bool> HasAnyAssignmentsAsync(CancellationToken cancellationToken) =>
        dbContext.SubjectRoleAssignments.AnyAsync(cancellationToken);

    public async Task<bool> TryBootstrapOwnerAsync(
        AccessSubject subject,
        string roleName,
        DateTimeOffset createdAtUtc,
        bool allowWhenAssignmentsExist,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        string normalizedRoleName = AccessControlRoleName.Normalize(roleName);

        if (!dbContext.Database.IsRelational())
        {
            if (!allowWhenAssignmentsExist && await this.HasAnyAssignmentsAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            await this.EnsureSubjectAsync(subject, createdAtUtc, cancellationToken).ConfigureAwait(false);
            await this.EnsureRoleAsync(normalizedRoleName, createdAtUtc, cancellationToken).ConfigureAwait(false);
            await this.EnsureRolePermissionAsync(normalizedRoleName, AccessControlPermissionGrant.OwnerWildcard, createdAtUtc, cancellationToken).ConfigureAwait(false);
            await this.EnsureRoleAssignmentAsync(subject, normalizedRoleName, AccessScope.Global, createdAtUtc, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        try
        {
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);

            if (!allowWhenAssignmentsExist)
            {
                int claimed = await dbContext.BootstrapState
                    .Where(state => state.Id == AccessBootstrapState.SingletonId && state.ClaimedBy == null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(state => state.ClaimedBy, subject.Id)
                        .SetProperty(state => state.ClaimedAtUtc, createdAtUtc), cancellationToken)
                    .ConfigureAwait(false);
                if (claimed == 0)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return false;
                }
            }

            await this.EnsureSubjectAsync(subject, createdAtUtc, cancellationToken).ConfigureAwait(false);
            await this.EnsureRoleAsync(normalizedRoleName, createdAtUtc, cancellationToken).ConfigureAwait(false);
            await this.EnsureRolePermissionAsync(
                    normalizedRoleName,
                    AccessControlPermissionGrant.OwnerWildcard,
                    createdAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            await this.EnsureRoleAssignmentAsync(
                    subject,
                    normalizedRoleName,
                    AccessScope.Global,
                    createdAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (
            !allowWhenAssignmentsExist && IsPostgreSqlSerializationFailure(exception))
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

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

        AccessScopeMatchOptions permissionMatchOptions = scopeMatchOptionsResolver.Resolve(permission);
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
                AccessScopeMatcher.GrantSatisfiesRequest(grant.Scope, scope, permissionMatchOptions))
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

        AccessScopeMatchOptions permissionMatchOptions = scopeMatchOptionsResolver.Resolve(permission);
        PersistedPermissionGrant[] grants = await this.QuerySubjectPermissionGrants(
                subject,
                permission,
                candidateScopeValues: null)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return grants
            .Select(grant => ToAccessGrantScope(grant, permissionMatchOptions))
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

    public Task EnsureRoleDefinitionAsync(
        string roleName,
        IReadOnlyCollection<string> permissions,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        string[] desiredPermissions = permissions
            .Select(AccessControlPermissionGrant.Normalize)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return this.ExecuteManagementWriteAsync(
            async token =>
            {
                await this.EnsureRoleAsync(normalizedRoleName, createdAtUtc, token).ConfigureAwait(false);
                AccessRole role = await this.GetRoleAsync(normalizedRoleName, token).ConfigureAwait(false);
                string[] currentPermissions = await dbContext.RolePermissions
                    .Where(permission => permission.RoleId == role.Id)
                    .Select(permission => permission.PermissionCode)
                    .ToArrayAsync(token)
                    .ConfigureAwait(false);

                foreach (string permission in currentPermissions.Except(
                             desiredPermissions,
                             StringComparer.Ordinal))
                {
                    AccessControlRemovalOutcome outcome = await this.RevokeRolePermissionCoreAsync(
                            normalizedRoleName,
                            permission,
                            token)
                        .ConfigureAwait(false);
                    if (outcome == AccessControlRemovalOutcome.LastOwnerProtected)
                    {
                        throw new InvalidOperationException(
                            "AccessControl protected the final owner role definition.");
                    }
                }

                foreach (string permission in desiredPermissions.Except(
                             currentPermissions,
                             StringComparer.Ordinal))
                {
                    dbContext.RolePermissions.Add(new AccessRolePermission(
                        idGenerator.NewId(),
                        role.Id,
                        permission,
                        createdAtUtc));
                }
            },
            cancellationToken);
    }

    public Task EnsureRoleAssignmentProvisionedAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);
        return this.ExecuteManagementWriteAsync(
            async token =>
            {
                await this.EnsureSubjectAsync(subject, createdAtUtc, token).ConfigureAwait(false);
                await this.EnsureRoleAssignmentAsync(subject, roleName, scope, createdAtUtc, token)
                    .ConfigureAwait(false);
            },
            cancellationToken);
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

    public Task<AccessControlRemovalOutcome> RevokeRolePermissionAsync(
        string roleName,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        string normalizedPermission = AccessControlPermissionGrant.Normalize(permissionCode);

        return this.ExecuteRemovalAsync(
            token => this.RevokeRolePermissionCoreAsync(normalizedRoleName, normalizedPermission, token),
            cancellationToken);
    }

    public Task<AccessControlRemovalOutcome> UnassignRoleAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);

        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        return this.ExecuteRemovalAsync(
            token => this.UnassignRoleCoreAsync(subject, normalizedRoleName, scope, token),
            cancellationToken);
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

    public async Task<AccessControlPage<AccessControlRoleDetails>> ListRolesPageAsync(
        PageRequest pageRequest,
        CancellationToken cancellationToken)
    {
        AccessControlRoleDetails[] rows = await dbContext.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .ThenBy(role => role.Id)
            .Skip(pageRequest.SkipCount)
            .Take(pageRequest.PageSize + 1)
            .Select(role => new AccessControlRoleDetails(
                role.Id,
                role.Name,
                role.Permissions.OrderBy(permission => permission.PermissionCode)
                    .Select(permission => permission.PermissionCode).ToArray(),
                role.Assignments.Count))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return new AccessControlPage<AccessControlRoleDetails>(
            rows.Take(pageRequest.PageSize).ToArray(),
            pageRequest.Page,
            pageRequest.PageSize,
            rows.Length > pageRequest.PageSize);
    }

    public async Task<IReadOnlyList<AccessControlRoleAssignmentDetails>> ListRoleAssignmentsAsync(
        string roleName,
        CancellationToken cancellationToken) =>
        await this.ListRoleAssignmentsAsync(roleName, scopeValue: null, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<AccessControlRoleAssignmentDetails>> ListRoleAssignmentsAsync(
        string roleName,
        AccessScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return await this.ListRoleAssignmentsAsync(roleName, scope.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccessControlPage<AccessControlRoleAssignmentDetails>> ListRoleAssignmentsPageAsync(
        string roleName,
        AccessScope scope,
        PageRequest pageRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        AccessControlRoleAssignmentProjection[] rows = await dbContext.SubjectRoleAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.Role != null &&
                assignment.Role.Name == normalizedRoleName &&
                assignment.ScopeValue == scope.Value)
            .OrderBy(assignment => assignment.SubjectKind)
            .ThenBy(assignment => assignment.SubjectId)
            .ThenBy(assignment => assignment.Id)
            .Skip(pageRequest.SkipCount)
            .Take(pageRequest.PageSize + 1)
            .Select(assignment => new AccessControlRoleAssignmentProjection(
                assignment.Id,
                assignment.SubjectKind,
                assignment.SubjectId,
                normalizedRoleName,
                assignment.ScopeValue,
                assignment.CreatedAtUtc))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = rows.Length > pageRequest.PageSize;
        AccessControlRoleAssignmentDetails[] items = rows
            .Take(pageRequest.PageSize)
            .Select(assignment => new AccessControlRoleAssignmentDetails(
                assignment.Id,
                ToSubjectKind(assignment.SubjectKind),
                assignment.SubjectId,
                assignment.RoleName,
                AccessScope.Parse(assignment.ScopeValue),
                assignment.CreatedAtUtc))
            .ToArray();
        return new AccessControlPage<AccessControlRoleAssignmentDetails>(
            items,
            pageRequest.Page,
            pageRequest.PageSize,
            hasMore);
    }

    public async Task<AccessControlPage<AccessControlRoleAssignmentDetails>> ListRoleAssignmentsPageAsync(
        string roleName,
        PageRequest pageRequest,
        CancellationToken cancellationToken)
    {
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        AccessControlRoleAssignmentProjection[] rows = await dbContext.SubjectRoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.Role != null && assignment.Role.Name == normalizedRoleName)
            .OrderBy(assignment => assignment.SubjectKind)
            .ThenBy(assignment => assignment.SubjectId)
            .ThenBy(assignment => assignment.ScopeValue)
            .ThenBy(assignment => assignment.Id)
            .Skip(pageRequest.SkipCount)
            .Take(pageRequest.PageSize + 1)
            .Select(assignment => new AccessControlRoleAssignmentProjection(
                assignment.Id,
                assignment.SubjectKind,
                assignment.SubjectId,
                normalizedRoleName,
                assignment.ScopeValue,
                assignment.CreatedAtUtc))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        AccessControlRoleAssignmentDetails[] items = rows.Take(pageRequest.PageSize)
            .Select(assignment => new AccessControlRoleAssignmentDetails(
                assignment.Id, ToSubjectKind(assignment.SubjectKind), assignment.SubjectId,
                assignment.RoleName, AccessScope.Parse(assignment.ScopeValue), assignment.CreatedAtUtc))
            .ToArray();
        return new AccessControlPage<AccessControlRoleAssignmentDetails>(
            items, pageRequest.Page, pageRequest.PageSize, rows.Length > pageRequest.PageSize);
    }

    private async Task<IReadOnlyList<AccessControlRoleAssignmentDetails>> ListRoleAssignmentsAsync(
        string roleName,
        string? scopeValue,
        CancellationToken cancellationToken)
    {
        string normalizedRoleName = AccessRole.NormalizeName(roleName);

        IQueryable<AccessSubjectRoleAssignment> query = dbContext.SubjectRoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.Role != null && assignment.Role.Name == normalizedRoleName);
        if (scopeValue is not null)
        {
            query = query.Where(assignment => assignment.ScopeValue == scopeValue);
        }

        AccessControlRoleAssignmentProjection[] assignments = await query
            .OrderBy(assignment => assignment.SubjectKind)
            .ThenBy(assignment => assignment.SubjectId)
            .ThenBy(assignment => assignment.ScopeValue)
            .Select(assignment => new AccessControlRoleAssignmentProjection(
                assignment.Id,
                assignment.SubjectKind,
                assignment.SubjectId,
                normalizedRoleName,
                assignment.ScopeValue,
                assignment.CreatedAtUtc))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return assignments
            .Select(assignment => new AccessControlRoleAssignmentDetails(
                assignment.Id,
                ToSubjectKind(assignment.SubjectKind),
                assignment.SubjectId,
                assignment.RoleName,
                AccessScope.Parse(assignment.ScopeValue),
                assignment.CreatedAtUtc))
            .ToArray();
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

        IQueryable<PersistedPermissionGrant> roleGrants = assignments
            .Join(
                permissionGrants,
                assignment => assignment.RoleId,
                permissionGrant => permissionGrant.RoleId,
                (assignment, permissionGrant) => new PersistedPermissionGrant
                {
                    SubjectKind = assignment.SubjectKind,
                    SubjectId = assignment.SubjectId,
                    ScopeValue = assignment.ScopeValue,
                    PermissionCode = permissionGrant.PermissionCode
                });

        IQueryable<Gma.Modules.AccessControl.Domain.Entities.AccessProfileAssignment> profileAssignments =
            dbContext.AccessProfileAssignments
                .AsNoTracking()
                .Where(assignment =>
                    assignment.SubjectKind == subjectKind &&
                    assignment.SubjectId == subject.Id &&
                    assignment.Profile != null &&
                    assignment.Profile.Status == DomainAccessProfileStatus.Active);
        if (candidateScopeValues is not null)
        {
            profileAssignments = profileAssignments.Where(assignment =>
                assignment.Profile != null &&
                candidateScopeValues.Contains(assignment.Profile.OwnerScopeValue));
        }

        IQueryable<Gma.Modules.AccessControl.Domain.Entities.AccessProfilePermission> profilePermissions =
            dbContext.AccessProfilePermissions
                .AsNoTracking()
                .Where(permissionGrant => permissionGrant.PermissionCode == permission.Value);
        IQueryable<PersistedPermissionGrant> profileGrants = profileAssignments.Join(
            profilePermissions,
            assignment => assignment.ProfileId,
            permissionGrant => permissionGrant.ProfileId,
            (assignment, permissionGrant) => new PersistedPermissionGrant
            {
                SubjectKind = assignment.SubjectKind,
                SubjectId = assignment.SubjectId,
                ScopeValue = assignment.Profile!.OwnerScopeValue,
                PermissionCode = permissionGrant.PermissionCode
            });

        return roleGrants.Concat(profileGrants);
    }

    private async Task<AccessControlRemovalOutcome> RevokeRolePermissionCoreAsync(
        string roleName,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        AccessRolePermission? permission = await dbContext.RolePermissions
            .SingleOrDefaultAsync(
                candidate => candidate.PermissionCode == permissionCode &&
                             candidate.Role != null &&
                             candidate.Role.Name == roleName,
                cancellationToken)
            .ConfigureAwait(false);
        if (permission is null)
        {
            return AccessControlRemovalOutcome.NotFound;
        }

        if (permission.PermissionCode == AccessControlPermissionGrant.OwnerWildcard &&
            !await this.HasOtherGlobalAdminOwnerAsync(
                    excludedRoleId: permission.RoleId,
                    excludedAssignmentId: null,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return AccessControlRemovalOutcome.LastOwnerProtected;
        }

        dbContext.RolePermissions.Remove(permission);
        return AccessControlRemovalOutcome.Removed;
    }

    private async Task<AccessControlRemovalOutcome> UnassignRoleCoreAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        CancellationToken cancellationToken)
    {
        int subjectKind = ToPersistedKind(subject);
        AccessSubjectRoleAssignment? assignment = await dbContext.SubjectRoleAssignments
            .SingleOrDefaultAsync(
                candidate => candidate.SubjectKind == subjectKind &&
                             candidate.SubjectId == subject.Id &&
                             candidate.ScopeValue == scope.Value &&
                             candidate.Role != null &&
                             candidate.Role.Name == roleName,
                cancellationToken)
            .ConfigureAwait(false);
        if (assignment is null)
        {
            return AccessControlRemovalOutcome.NotFound;
        }

        bool isGlobalAdminOwner = assignment.SubjectKind == (int)AccessSubjectKind.AdminActor &&
                                  assignment.ScopeValue == AccessScope.Global.Value &&
                                  await dbContext.RolePermissions.AnyAsync(
                                      permission => permission.RoleId == assignment.RoleId &&
                                                    permission.PermissionCode == AccessControlPermissionGrant.OwnerWildcard,
                                      cancellationToken).ConfigureAwait(false);
        if (isGlobalAdminOwner &&
            !await this.HasOtherGlobalAdminOwnerAsync(
                    excludedRoleId: null,
                    excludedAssignmentId: assignment.Id,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return AccessControlRemovalOutcome.LastOwnerProtected;
        }

        dbContext.SubjectRoleAssignments.Remove(assignment);
        return AccessControlRemovalOutcome.Removed;
    }

    private async Task<bool> HasOtherGlobalAdminOwnerAsync(
        Guid? excludedRoleId,
        Guid? excludedAssignmentId,
        CancellationToken cancellationToken)
    {
        IQueryable<AccessSubjectRoleAssignment> assignments = dbContext.SubjectRoleAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.SubjectKind == (int)AccessSubjectKind.AdminActor &&
                assignment.ScopeValue == AccessScope.Global.Value);
        if (excludedAssignmentId.HasValue)
        {
            Guid assignmentId = excludedAssignmentId.Value;
            assignments = assignments.Where(assignment => assignment.Id != assignmentId);
        }

        IQueryable<AccessRolePermission> ownerPermissions = dbContext.RolePermissions
            .AsNoTracking()
            .Where(permission => permission.PermissionCode == AccessControlPermissionGrant.OwnerWildcard);
        if (excludedRoleId.HasValue)
        {
            Guid roleId = excludedRoleId.Value;
            ownerPermissions = ownerPermissions.Where(permission => permission.RoleId != roleId);
        }

        return await assignments
            .Join(
                ownerPermissions,
                assignment => assignment.RoleId,
                permission => permission.RoleId,
                static (_, _) => 1)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AccessControlRemovalOutcome> ExecuteRemovalAsync(
        Func<CancellationToken, Task<AccessControlRemovalOutcome>> remove,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            AccessControlRemovalOutcome inMemoryOutcome = await remove(cancellationToken).ConfigureAwait(false);
            if (inMemoryOutcome == AccessControlRemovalOutcome.Removed)
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return inMemoryOutcome;
        }

        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await this.ExecuteRemovalWithinTransactionAsync(remove, cancellationToken).ConfigureAwait(false);
        }

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        AccessControlRemovalOutcome outcome = await this.ExecuteRemovalWithinTransactionAsync(
                remove,
                cancellationToken)
            .ConfigureAwait(false);
        if (outcome == AccessControlRemovalOutcome.Removed)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }

        return outcome;
    }

    private async Task<AccessControlRemovalOutcome> ExecuteRemovalWithinTransactionAsync(
        Func<CancellationToken, Task<AccessControlRemovalOutcome>> remove,
        CancellationToken cancellationToken)
    {
        int managementLockAcquired = await dbContext.BootstrapState
            .Where(state => state.Id == AccessBootstrapState.SingletonId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    state => state.ManagementRevision,
                    state => state.ManagementRevision + 1),
                cancellationToken)
            .ConfigureAwait(false);
        if (managementLockAcquired != 1)
        {
            throw new InvalidOperationException("The access-control management safety lock is unavailable.");
        }

        AccessControlRemovalOutcome outcome = await remove(cancellationToken).ConfigureAwait(false);
        if (outcome == AccessControlRemovalOutcome.Removed)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return outcome;
    }

    private async Task ExecuteManagementWriteAsync(
        Func<CancellationToken, Task> mutate,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            await mutate(cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (dbContext.Database.CurrentTransaction is not null)
        {
            await this.AcquireManagementLockAsync(cancellationToken).ConfigureAwait(false);
            await mutate(cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        await this.AcquireManagementLockAsync(cancellationToken).ConfigureAwait(false);
        await mutate(cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task AcquireManagementLockAsync(CancellationToken cancellationToken)
    {
        int managementLockAcquired = await dbContext.BootstrapState
            .Where(state => state.Id == AccessBootstrapState.SingletonId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    state => state.ManagementRevision,
                    state => state.ManagementRevision + 1),
                cancellationToken)
            .ConfigureAwait(false);
        if (managementLockAcquired != 1)
        {
            throw new InvalidOperationException("The access-control management safety lock is unavailable.");
        }
    }

    private static AccessGrantScope ToAccessGrantScope(
        PersistedPermissionGrant grant,
        AccessScopeMatchOptions permissionMatchOptions) =>
        grant.PermissionCode == AccessControlPermissionGrant.OwnerWildcard
            ? new AccessGrantScope(grant.Scope, OwnerWildcardScopeMatchOptions)
            : new AccessGrantScope(grant.Scope, permissionMatchOptions);

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

    private static bool IsPostgreSqlSerializationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres &&
                string.Equals(postgres.SqlState, PostgresErrorCodes.SerializationFailure, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static AccessSubjectKind ToSubjectKind(int persistedKind)
    {
        AccessSubjectKind kind = (AccessSubjectKind)persistedKind;
        if (kind == AccessSubjectKind.Unknown || !Enum.IsDefined(kind))
        {
            throw new InvalidOperationException($"Persisted access subject kind '{persistedKind}' is invalid.");
        }

        return kind;
    }

    private sealed class PersistedPermissionGrant
    {
        public int SubjectKind { get; init; }
        public string SubjectId { get; init; } = string.Empty;
        public string ScopeValue { get; init; } = string.Empty;
        public string PermissionCode { get; init; } = string.Empty;
        public AccessScope Scope => AccessScope.Parse(this.ScopeValue);
    }

    private sealed record AccessControlRoleAssignmentProjection(
        Guid Id,
        int SubjectKind,
        string SubjectId,
        string RoleName,
        string ScopeValue,
        DateTimeOffset CreatedAtUtc);
}
