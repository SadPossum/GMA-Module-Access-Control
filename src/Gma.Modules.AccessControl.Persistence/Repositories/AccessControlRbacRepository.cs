namespace Gma.Modules.AccessControl.Persistence.Repositories;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Framework.Pagination;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Persistence;
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
    IAccessScopeMatchOptionsResolver scopeMatchOptionsResolver,
    ISystemClock clock)
    : IAccessControlRbacRepository
{
    private const int PermissionQuerySubjectBatchSize = 500;

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

        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await this.TryBootstrapOwnerWithinTransactionAsync(
                    subject,
                    normalizedRoleName,
                    createdAtUtc,
                    allowWhenAssignmentsExist,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);

            bool bootstrapped = await this.TryBootstrapOwnerWithinTransactionAsync(
                    subject,
                    normalizedRoleName,
                    createdAtUtc,
                    allowWhenAssignmentsExist,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!bootstrapped)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

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

    private async Task<bool> TryBootstrapOwnerWithinTransactionAsync(
        AccessSubject subject,
        string normalizedRoleName,
        DateTimeOffset createdAtUtc,
        bool allowWhenAssignmentsExist,
        CancellationToken cancellationToken)
    {
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
        return true;
    }

    public Task<bool> RoleExistsAsync(string roleName, CancellationToken cancellationToken)
    {
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        return dbContext.Roles.AnyAsync(role => role.Name == normalizedRoleName, cancellationToken);
    }

    public Task<string[]?> FindRolePermissionsAsync(
        string roleName,
        CancellationToken cancellationToken)
    {
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        return dbContext.Roles
            .AsNoTracking()
            .Where(role => role.Name == normalizedRoleName)
            .Select(role => role.Permissions
                .OrderBy(permission => permission.PermissionCode)
                .Select(permission => permission.PermissionCode)
                .ToArray())
            .SingleOrDefaultAsync(cancellationToken);
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

    public async Task<bool> RoleHasActiveTemporaryAssignmentsAsync(
        string roleName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        long nowUnixMilliseconds = nowUtc.ToUniversalTime().ToUnixTimeMilliseconds();

        return await dbContext.SubjectRoleAssignments
            .AnyAsync(
                assignment =>
                    assignment.Role != null &&
                    assignment.Role.Name == normalizedRoleName &&
                    assignment.RevokedAtUnixMilliseconds == null &&
                    assignment.ExpiresAtUnixMilliseconds != null &&
                    assignment.ExpiresAtUnixMilliseconds > nowUnixMilliseconds,
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
        string scopeHash = AccessScopeIndex.Create(scope.Value);
        DateTimeOffset now = clock.UtcNow;
        long nowUnixMilliseconds = now.ToUnixTimeMilliseconds();
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
                assignment.ScopeHash == scopeHash &&
                assignment.ScopeValue == scope.Value &&
                assignment.IsActiveAt(now)))
        {
            return true;
        }

        return await dbContext.SubjectRoleAssignments
            .AnyAsync(assignment =>
                assignment.SubjectKind == subjectKind &&
                assignment.SubjectId == subject.Id &&
                assignment.ScopeHash == scopeHash &&
                assignment.ScopeValue == scope.Value &&
                assignment.RevokedAtUnixMilliseconds == null &&
                (assignment.ExpiresAtUnixMilliseconds == null ||
                 assignment.ExpiresAtUnixMilliseconds > nowUnixMilliseconds) &&
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

        IReadOnlyList<bool> decisions = await this.HasPermissionsAsync(
                [new AccessRequirement(subject, permission, scope)],
                cancellationToken)
            .ConfigureAwait(false);
        return decisions[0];
    }

    public async Task<IReadOnlyList<bool>> HasPermissionsAsync(
        IReadOnlyList<AccessRequirement> requirements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        if (requirements.Any(requirement => requirement is null))
        {
            throw new ArgumentException(
                "Access requirements cannot contain null values.",
                nameof(requirements));
        }

        bool[] decisions = new bool[requirements.Count];
        IndexedAccessRequirement[] indexed = requirements
            .Select((requirement, index) => new IndexedAccessRequirement(index, requirement))
            .ToArray();
        foreach (IGrouping<AccessRequirementGroupKey, IndexedAccessRequirement> group in indexed.GroupBy(item =>
                     new AccessRequirementGroupKey(
                         item.Requirement.Subject.Kind,
                         item.Requirement.Scope.Value)))
        {
            IndexedAccessRequirement first = group.First();
            PermissionCode[] permissions = group
                .Select(item => item.Requirement.Permission)
                .Distinct()
                .ToArray();
            string[][] subjectBatches = group
                .Select(item => item.Requirement.Subject.Id)
                .Distinct(StringComparer.Ordinal)
                .Chunk(PermissionQuerySubjectBatchSize)
                .ToArray();
            List<PersistedPermissionGrant> candidateGrants = [];
            foreach (string[] subjectIds in subjectBatches)
            {
                PersistedPermissionGrant[] batchGrants = await this.QuerySubjectPermissionGrants(
                        first.Requirement.Subject.Kind,
                        subjectIds,
                        permissions,
                        GetCandidateScopeValues(first.Requirement.Scope))
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
                candidateGrants.AddRange(batchGrants);
            }

            foreach (IndexedAccessRequirement item in group)
            {
                AccessScopeMatchOptions permissionMatchOptions = scopeMatchOptionsResolver.Resolve(
                    item.Requirement.Permission);
                decisions[item.Index] = candidateGrants.Any(grant =>
                    grant.SubjectId == item.Requirement.Subject.Id &&
                    (grant.PermissionCode == AccessControlPermissionGrant.OwnerWildcard
                        ? AccessScopeMatcher.GrantSatisfiesRequest(
                            grant.Scope,
                            item.Requirement.Scope,
                            OwnerWildcardScopeMatchOptions)
                        : grant.PermissionCode == item.Requirement.Permission.Value &&
                          AccessScopeMatcher.GrantSatisfiesRequest(
                              grant.Scope,
                              item.Requirement.Scope,
                              permissionMatchOptions)));
            }
        }

        return decisions;
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
                subject.Kind,
                [subject.Id],
                [permission],
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

        AccessControlRolePermissionGrantPersistenceOutcome outcome = await this
            .GrantRolePermissionAsync(roleName, permissionCode, createdAtUtc, cancellationToken)
            .ConfigureAwait(false);
        if (outcome == AccessControlRolePermissionGrantPersistenceOutcome.TemporaryAssignmentsExist)
        {
            throw new InvalidOperationException(
                "AccessControl cannot expand a role while active temporary assignments exist.");
        }
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
                string[] additions = desiredPermissions
                    .Except(currentPermissions, StringComparer.Ordinal)
                    .ToArray();
                if (additions.Length > 0)
                {
                    await this.ThrowIfRolePermissionExpansionWouldAffectTemporaryAssignmentsAsync(
                            normalizedRoleName,
                            createdAtUtc,
                            token)
                        .ConfigureAwait(false);
                }

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

                foreach (string permission in additions)
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

    public Task<AccessControlRolePermissionGrantPersistenceOutcome> GrantRolePermissionAsync(
        string roleName,
        string permissionCode,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        string normalizedPermission = AccessControlPermissionGrant.Normalize(permissionCode);
        return this.ExecuteManagementWriteAsync(
            async token =>
            {
                if (await this.RoleHasActiveTemporaryAssignmentsAsync(
                        normalizedRoleName,
                        createdAtUtc,
                        token)
                    .ConfigureAwait(false))
                {
                    return AccessControlRolePermissionGrantPersistenceOutcome.TemporaryAssignmentsExist;
                }

                AccessRole role = await this.GetRoleAsync(normalizedRoleName, token).ConfigureAwait(false);
                if (await dbContext.RolePermissions.AnyAsync(
                        permission =>
                            permission.RoleId == role.Id &&
                            permission.PermissionCode == normalizedPermission,
                        token)
                    .ConfigureAwait(false))
                {
                    return AccessControlRolePermissionGrantPersistenceOutcome.AlreadyGranted;
                }

                dbContext.RolePermissions.Add(new AccessRolePermission(
                    idGenerator.NewId(),
                    role.Id,
                    normalizedPermission,
                    createdAtUtc));
                return AccessControlRolePermissionGrantPersistenceOutcome.Granted;
            },
            cancellationToken);
    }

    private async Task ThrowIfRolePermissionExpansionWouldAffectTemporaryAssignmentsAsync(
        string roleName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (await this.RoleHasActiveTemporaryAssignmentsAsync(
                    roleName,
                    nowUtc,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "AccessControl cannot expand a role while active temporary assignments exist.");
        }
    }

    public async Task AssignRoleAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken) =>
        await this.AssignRoleAsync(
                subject,
                roleName,
                scope,
                createdAtUtc,
                expiresAtUtc: null,
                cancellationToken)
            .ConfigureAwait(false);

    public Task<AccessControlRoleAssignmentPersistenceOutcome> TryAssignRoleAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc,
        IReadOnlyCollection<string> expectedRolePermissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(expectedRolePermissions);
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        string[] expectedPermissions = expectedRolePermissions
            .Select(AccessControlPermissionGrant.Normalize)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return this.ExecuteManagementWriteAsync(
            async token =>
            {
                string[]? currentPermissions = await this
                    .FindRolePermissionsAsync(normalizedRoleName, token)
                    .ConfigureAwait(false);
                if (currentPermissions is null ||
                    !currentPermissions.SequenceEqual(expectedPermissions, StringComparer.Ordinal))
                {
                    return AccessControlRoleAssignmentPersistenceOutcome.RoleDefinitionChanged;
                }

                if (await this.AssignmentExistsAsync(subject, normalizedRoleName, scope, token)
                    .ConfigureAwait(false))
                {
                    return AccessControlRoleAssignmentPersistenceOutcome.AlreadyExists;
                }

                await this.EnsureSubjectAsync(subject, createdAtUtc, token).ConfigureAwait(false);
                await this.AssignRoleAsync(
                        subject,
                        normalizedRoleName,
                        scope,
                        createdAtUtc,
                        expiresAtUtc,
                        token)
                    .ConfigureAwait(false);
                return AccessControlRoleAssignmentPersistenceOutcome.Assigned;
            },
            cancellationToken);
    }

    private async Task AssignRoleAsync(
        AccessSubject subject,
        string roleName,
        AccessScope scope,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc,
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
            createdAtUtc,
            expiresAtUtc));
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
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);

        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        return this.ExecuteRemovalAsync(
            token => this.UnassignRoleCoreAsync(
                subject,
                normalizedRoleName,
                scope,
                revokedAtUtc,
                token),
            cancellationToken);
    }

    public async Task<IReadOnlyList<AccessControlRoleDetails>> ListRolesAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        long nowUnixMilliseconds = now.ToUnixTimeMilliseconds();
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
                role.Assignments.Count(assignment =>
                    assignment.RevokedAtUnixMilliseconds == null &&
                    (assignment.ExpiresAtUnixMilliseconds == null ||
                     assignment.ExpiresAtUnixMilliseconds > nowUnixMilliseconds))))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AccessControlPage<AccessControlRoleDetails>> ListRolesPageAsync(
        PageRequest pageRequest,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        long nowUnixMilliseconds = now.ToUnixTimeMilliseconds();
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
                role.Assignments.Count(assignment =>
                    assignment.RevokedAtUnixMilliseconds == null &&
                    (assignment.ExpiresAtUnixMilliseconds == null ||
                     assignment.ExpiresAtUnixMilliseconds > nowUnixMilliseconds))))
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
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        string scopeHash = AccessScopeIndex.Create(scope.Value);
        DateTimeOffset now = clock.UtcNow;
        IQueryable<AccessSubjectRoleAssignment> query = dbContext.SubjectRoleAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.Role != null &&
                assignment.Role.Name == normalizedRoleName &&
                assignment.ScopeHash == scopeHash &&
                assignment.ScopeValue == scope.Value);
        if (!includeInactive)
        {
            query = ActiveAssignments(query, now.ToUnixTimeMilliseconds());
        }

        AccessControlRoleAssignmentProjection[] rows = await query
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
                assignment.CreatedAtUtc,
                assignment.ExpiresAtUnixMilliseconds,
                assignment.RevokedAtUnixMilliseconds))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = rows.Length > pageRequest.PageSize;
        AccessControlRoleAssignmentDetails[] items = rows
            .Take(pageRequest.PageSize)
            .Select(assignment => ToAssignmentDetails(assignment, now))
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
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        string normalizedRoleName = AccessRole.NormalizeName(roleName);
        DateTimeOffset now = clock.UtcNow;
        IQueryable<AccessSubjectRoleAssignment> query = dbContext.SubjectRoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.Role != null && assignment.Role.Name == normalizedRoleName);
        if (!includeInactive)
        {
            query = ActiveAssignments(query, now.ToUnixTimeMilliseconds());
        }

        AccessControlRoleAssignmentProjection[] rows = await query
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
                assignment.CreatedAtUtc,
                assignment.ExpiresAtUnixMilliseconds,
                assignment.RevokedAtUnixMilliseconds))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        AccessControlRoleAssignmentDetails[] items = rows.Take(pageRequest.PageSize)
            .Select(assignment => ToAssignmentDetails(assignment, now))
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
        DateTimeOffset now = clock.UtcNow;

        IQueryable<AccessSubjectRoleAssignment> query = ActiveAssignments(
            dbContext.SubjectRoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.Role != null && assignment.Role.Name == normalizedRoleName),
            now.ToUnixTimeMilliseconds());
        if (scopeValue is not null)
        {
            string scopeHash = AccessScopeIndex.Create(scopeValue);
            query = query.Where(assignment =>
                assignment.ScopeHash == scopeHash &&
                assignment.ScopeValue == scopeValue);
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
                assignment.CreatedAtUtc,
                assignment.ExpiresAtUnixMilliseconds,
                assignment.RevokedAtUnixMilliseconds))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return assignments
            .Select(assignment => ToAssignmentDetails(assignment, now))
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
        AccessSubjectKind subjectKind,
        IReadOnlyCollection<string> subjectIds,
        IReadOnlyCollection<PermissionCode> permissions,
        string[]? candidateScopeValues)
    {
        int persistedSubjectKind = ToPersistedKind(subjectKind);
        string[] requestedPermissionCodes = permissions
            .Select(permission => permission.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] candidatePermissionCodes = requestedPermissionCodes
            .Append(AccessControlPermissionGrant.OwnerWildcard)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        long nowUnixMilliseconds = clock.UtcNow.ToUnixTimeMilliseconds();

        IQueryable<AccessSubjectRoleAssignment> assignments = dbContext.SubjectRoleAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.SubjectKind == persistedSubjectKind &&
                subjectIds.Contains(assignment.SubjectId) &&
                assignment.RevokedAtUnixMilliseconds == null &&
                (assignment.ExpiresAtUnixMilliseconds == null ||
                 assignment.ExpiresAtUnixMilliseconds > nowUnixMilliseconds));

        if (candidateScopeValues is not null)
        {
            string[] candidateScopeHashes = candidateScopeValues
                .Select(AccessScopeIndex.Create)
                .ToArray();
            assignments = assignments.Where(assignment =>
                candidateScopeHashes.Contains(assignment.ScopeHash) &&
                candidateScopeValues.Contains(assignment.ScopeValue));
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
                    assignment.SubjectKind == persistedSubjectKind &&
                    subjectIds.Contains(assignment.SubjectId) &&
                    assignment.Profile != null &&
                    assignment.Profile.Status == DomainAccessProfileStatus.Active);
        if (candidateScopeValues is not null)
        {
            profileAssignments = profileAssignments.Where(assignment =>
                candidateScopeValues.Contains(assignment.AssignmentScopeValue));
        }

        IQueryable<Gma.Modules.AccessControl.Domain.Entities.AccessProfilePermission> profilePermissions =
            dbContext.AccessProfilePermissions
                .AsNoTracking()
                .Where(permissionGrant => requestedPermissionCodes.Contains(permissionGrant.PermissionCode));
        IQueryable<PersistedPermissionGrant> profileGrants = profileAssignments.Join(
            profilePermissions,
            assignment => assignment.ProfileId,
            permissionGrant => permissionGrant.ProfileId,
            (assignment, permissionGrant) => new PersistedPermissionGrant
            {
                SubjectKind = assignment.SubjectKind,
                SubjectId = assignment.SubjectId,
                ScopeValue = assignment.AssignmentScopeValue,
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
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken)
    {
        int subjectKind = ToPersistedKind(subject);
        string scopeHash = AccessScopeIndex.Create(scope.Value);
        long nowUnixMilliseconds = clock.UtcNow.ToUnixTimeMilliseconds();
        AccessSubjectRoleAssignment? assignment = await dbContext.SubjectRoleAssignments
            .SingleOrDefaultAsync(
                candidate => candidate.SubjectKind == subjectKind &&
                             candidate.SubjectId == subject.Id &&
                             candidate.ScopeHash == scopeHash &&
                             candidate.ScopeValue == scope.Value &&
                             candidate.RevokedAtUnixMilliseconds == null &&
                             (candidate.ExpiresAtUnixMilliseconds == null ||
                              candidate.ExpiresAtUnixMilliseconds > nowUnixMilliseconds) &&
                             candidate.Role != null &&
                             candidate.Role.Name == roleName,
                cancellationToken)
            .ConfigureAwait(false);
        if (assignment is null)
        {
            return AccessControlRemovalOutcome.NotFound;
        }

        string globalScopeHash = AccessScopeIndex.Create(AccessScope.Global.Value);
        bool isGlobalAdminOwner = assignment.SubjectKind == (int)AccessSubjectKind.AdminActor &&
                                  assignment.ScopeHash == globalScopeHash &&
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

        assignment.Revoke(revokedAtUtc);
        return AccessControlRemovalOutcome.Removed;
    }

    private async Task<bool> HasOtherGlobalAdminOwnerAsync(
        Guid? excludedRoleId,
        Guid? excludedAssignmentId,
        CancellationToken cancellationToken)
    {
        long nowUnixMilliseconds = clock.UtcNow.ToUnixTimeMilliseconds();
        string globalScopeHash = AccessScopeIndex.Create(AccessScope.Global.Value);
        IQueryable<AccessSubjectRoleAssignment> assignments = dbContext.SubjectRoleAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.SubjectKind == (int)AccessSubjectKind.AdminActor &&
                assignment.ScopeHash == globalScopeHash &&
                assignment.ScopeValue == AccessScope.Global.Value &&
                assignment.RevokedAtUnixMilliseconds == null &&
                (assignment.ExpiresAtUnixMilliseconds == null ||
                 assignment.ExpiresAtUnixMilliseconds > nowUnixMilliseconds));
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
        await AccessControlManagementLock.AcquireAsync(dbContext, cancellationToken).ConfigureAwait(false);

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

    private async Task<T> ExecuteManagementWriteAsync<T>(
        Func<CancellationToken, Task<T>> mutate,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            T result = await mutate(cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        if (dbContext.Database.CurrentTransaction is not null)
        {
            await this.AcquireManagementLockAsync(cancellationToken).ConfigureAwait(false);
            T result = await mutate(cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        await this.AcquireManagementLockAsync(cancellationToken).ConfigureAwait(false);
        T mutationResult = await mutate(cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return mutationResult;
    }

    private async Task AcquireManagementLockAsync(CancellationToken cancellationToken)
    {
        await AccessControlManagementLock.AcquireAsync(dbContext, cancellationToken).ConfigureAwait(false);
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
        => ToPersistedKind(subject.Kind);

    private static int ToPersistedKind(AccessSubjectKind subjectKind)
    {
        if (subjectKind == AccessSubjectKind.Unknown || !Enum.IsDefined(subjectKind))
        {
            throw new ArgumentException(
                "Access subject kind must be a defined non-unknown value.",
                nameof(subjectKind));
        }

        return (int)subjectKind;
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

    private static IQueryable<AccessSubjectRoleAssignment> ActiveAssignments(
        IQueryable<AccessSubjectRoleAssignment> assignments,
        long nowUnixMilliseconds) =>
        assignments.Where(assignment =>
            assignment.RevokedAtUnixMilliseconds == null &&
            (assignment.ExpiresAtUnixMilliseconds == null ||
             assignment.ExpiresAtUnixMilliseconds > nowUnixMilliseconds));

    private static AccessControlRoleAssignmentDetails ToAssignmentDetails(
        AccessControlRoleAssignmentProjection assignment,
        DateTimeOffset nowUtc) =>
        new(
            assignment.Id,
            ToSubjectKind(assignment.SubjectKind),
            assignment.SubjectId,
            assignment.RoleName,
            AccessScope.Parse(assignment.ScopeValue),
            assignment.CreatedAtUtc,
            ToDateTimeOffset(assignment.ExpiresAtUnixMilliseconds),
            ToDateTimeOffset(assignment.RevokedAtUnixMilliseconds),
            assignment.RevokedAtUnixMilliseconds is not null
                ? AccessRoleAssignmentStatus.Revoked
                : assignment.ExpiresAtUnixMilliseconds is not null &&
                  assignment.ExpiresAtUnixMilliseconds <= nowUtc.ToUnixTimeMilliseconds()
                    ? AccessRoleAssignmentStatus.Expired
                    : AccessRoleAssignmentStatus.Active);

    private static DateTimeOffset? ToDateTimeOffset(long? unixMilliseconds) =>
        unixMilliseconds is null
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds.Value);

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
        DateTimeOffset CreatedAtUtc,
        long? ExpiresAtUnixMilliseconds,
        long? RevokedAtUnixMilliseconds);

    private sealed record IndexedAccessRequirement(int Index, AccessRequirement Requirement);

    private sealed record AccessRequirementGroupKey(
        AccessSubjectKind SubjectKind,
        string ScopeValue);
}
