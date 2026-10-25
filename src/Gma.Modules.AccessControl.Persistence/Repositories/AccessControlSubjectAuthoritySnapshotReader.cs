namespace Gma.Modules.AccessControl.Persistence.Repositories;

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

internal sealed class AccessControlSubjectAuthoritySnapshotReader(
    AccessControlDbContext dbContext,
    TimeProvider? timeProvider = null)
    : IAccessControlSubjectAuthoritySnapshotReader
{
    private const int ContractVersion = 2;
    private const string DigestDomain =
        "gma.access-control.subject-authority-snapshot.v2";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<AccessControlSubjectAuthoritySnapshot> ReadAsync(
        AccessControlSubjectAuthoritySnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DateTimeOffset observedAtUtc = this.clock.GetUtcNow();
        if (!AccessSubject.TryCreate(
                request.SubjectKind,
                request.SubjectId,
                out AccessSubject? subject) ||
            request.RootScope is null ||
            !TryNormalizeExpectedTargets(
                request.RootScope,
                request.ExpectedTargetScopes,
                out AccessScope[] expectedTargetScopes,
                out string[] lifecycleScopeValues))
        {
            return Empty(
                AccessControlAuthoritySnapshotStatus.Invalid,
                observedAtUtc);
        }

        try
        {
            long? before = await ReadRevisionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (before is null)
            {
                return Empty(
                    AccessControlAuthoritySnapshotStatus.Unavailable,
                    observedAtUtc,
                    subject: subject,
                    rootScope: request.RootScope,
                    expectedTargetScopes: expectedTargetScopes);
            }

            long nowMilliseconds = observedAtUtc.ToUnixTimeMilliseconds();
            RoleAssignmentRow[] roleRows = await dbContext
                .SubjectRoleAssignments
                .AsNoTracking()
                .Where(assignment =>
                    assignment.SubjectKind == (int)subject.Kind &&
                    assignment.SubjectId == subject.Id &&
                    assignment.RevokedAtUnixMilliseconds == null &&
                    (assignment.ExpiresAtUnixMilliseconds == null ||
                     assignment.ExpiresAtUnixMilliseconds > nowMilliseconds))
                .Select(assignment => new RoleAssignmentRow(
                    assignment.Id,
                    assignment.RoleId,
                    assignment.ScopeValue,
                    assignment.ExpiresAtUnixMilliseconds))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!TrySelectRelevant(roleRows, request.RootScope, out roleRows))
            {
                return Empty(
                    AccessControlAuthoritySnapshotStatus.Invalid,
                    observedAtUtc,
                    before,
                    subject,
                    request.RootScope,
                    expectedTargetScopes);
            }

            Guid[] roleIds = roleRows
                .Select(row => row.RoleId)
                .Distinct()
                .ToArray();
            RoleRow[] roles = roleIds.Length == 0
                ? []
                : await dbContext.Roles
                    .AsNoTracking()
                    .Where(role => roleIds.Contains(role.Id))
                    .Select(role => new RoleRow(role.Id, role.Name))
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
            RolePermissionRow[] rolePermissions = roleIds.Length == 0
                ? []
                : await dbContext.RolePermissions
                    .AsNoTracking()
                    .Where(permission => roleIds.Contains(permission.RoleId))
                    .Select(permission => new RolePermissionRow(
                        permission.RoleId,
                        permission.PermissionCode))
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

            ProfileAssignmentRow[] profileRows = await dbContext
                .AccessProfileAssignments
                .AsNoTracking()
                .Where(assignment =>
                    assignment.SubjectKind == (int)subject.Kind &&
                    assignment.SubjectId == subject.Id)
                .Select(assignment => new ProfileAssignmentRow(
                    assignment.Id,
                    assignment.ProfileId,
                    assignment.AssignmentScopeValue))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!TrySelectRelevant(
                    profileRows,
                    request.RootScope,
                    out profileRows))
            {
                return Empty(
                    AccessControlAuthoritySnapshotStatus.Invalid,
                    observedAtUtc,
                    before,
                    subject,
                    request.RootScope,
                    expectedTargetScopes);
            }

            Guid[] profileIds = profileRows
                .Select(row => row.ProfileId)
                .Distinct()
                .ToArray();
            ProfileRow[] profiles = profileIds.Length == 0
                ? []
                : await dbContext.AccessProfiles
                    .AsNoTracking()
                    .Where(profile => profileIds.Contains(profile.Id))
                    .Select(profile => new ProfileRow(
                        profile.Id,
                        profile.OwnerScopeValue,
                        profile.Key,
                        (int)profile.Status,
                        profile.Version))
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
            ProfilePermissionRow[] profilePermissions = profileIds.Length == 0
                ? []
                : await dbContext.AccessProfilePermissions
                    .AsNoTracking()
                    .Where(permission => profileIds.Contains(permission.ProfileId))
                    .Select(permission => new ProfilePermissionRow(
                        permission.ProfileId,
                        permission.PermissionCode))
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

            bool scopesOpen = await dbContext.AreScopesOpenAsync(
                    lifecycleScopeValues,
                    cancellationToken)
                .ConfigureAwait(false);

            long? after = await ReadRevisionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (after is null)
            {
                return Empty(
                    AccessControlAuthoritySnapshotStatus.Unavailable,
                    observedAtUtc,
                    before,
                    subject,
                    request.RootScope,
                    expectedTargetScopes);
            }

            if (after != before)
            {
                return Empty(
                    AccessControlAuthoritySnapshotStatus.Stale,
                    observedAtUtc,
                    before,
                    subject,
                    request.RootScope,
                    expectedTargetScopes);
            }

            if (!TryMapRoleGrants(
                    roleRows,
                    roles,
                    rolePermissions,
                    out AccessControlAuthorityRoleGrant[] roleGrants) ||
                !TryMapProfileGrants(
                    profileRows,
                    profiles,
                    profilePermissions,
                    out AccessControlAuthorityProfileGrant[] profileGrants))
            {
                return Empty(
                    AccessControlAuthoritySnapshotStatus.Invalid,
                    observedAtUtc,
                    after,
                    subject,
                    request.RootScope,
                    expectedTargetScopes);
            }

            string factsSha256 = Hash(
                subject,
                request.RootScope,
                expectedTargetScopes,
                after.Value,
                roleGrants,
                profileGrants);
            DateTimeOffset? earliestExpiryAtUtc = roleGrants
                .Where(grant => grant.ExpiresAtUtc is not null)
                .Select(grant => grant.ExpiresAtUtc)
                .Min();
            return new AccessControlSubjectAuthoritySnapshot(
                !scopesOpen
                    ? AccessControlAuthoritySnapshotStatus.Closed
                    : AccessControlAuthoritySnapshotStatus.Stable,
                ContractVersion,
                subject.Kind,
                subject.Id,
                request.RootScope,
                expectedTargetScopes,
                after,
                observedAtUtc,
                earliestExpiryAtUtc,
                factsSha256,
                roleGrants,
                profileGrants);
        }
        catch (InvalidDataException)
        {
            return Empty(
                AccessControlAuthoritySnapshotStatus.Invalid,
                observedAtUtc,
                subject: subject,
                rootScope: request.RootScope,
                expectedTargetScopes: expectedTargetScopes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Empty(
                AccessControlAuthoritySnapshotStatus.Unavailable,
                observedAtUtc,
                subject: subject,
                rootScope: request.RootScope,
                expectedTargetScopes: expectedTargetScopes);
        }
    }

    private async Task<long?> ReadRevisionAsync(
        CancellationToken cancellationToken) =>
        await dbContext.BootstrapState
            .AsNoTracking()
            .Where(state => state.Id == AccessBootstrapState.SingletonId)
            .Select(state => (long?)state.ManagementRevision)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private static bool TryNormalizeExpectedTargets(
        AccessScope rootScope,
        IReadOnlyCollection<AccessScope>? source,
        out AccessScope[] expectedTargetScopes,
        out string[] lifecycleScopeValues)
    {
        expectedTargetScopes = [];
        lifecycleScopeValues = [];
        if (source is null ||
            source.Count is < 1 or >
                IAccessControlSubjectAuthoritySnapshotReader
                    .MaximumExpectedTargetScopeCount ||
            source.Any(scope => scope is null))
        {
            return false;
        }

        AccessScope[] normalized = source
            .OrderBy(scope => scope.Value, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Select(scope => scope.Value)
                .Distinct(StringComparer.Ordinal).Count() != normalized.Length ||
            normalized.Any(scope => !IsPrefix(rootScope, scope)))
        {
            return false;
        }

        expectedTargetScopes = normalized;
        lifecycleScopeValues = normalized
            .Select(scope => scope.Value)
            .ToArray();
        return true;
    }

    private static bool TrySelectRelevant(
        RoleAssignmentRow[] source,
        AccessScope rootScope,
        out RoleAssignmentRow[] relevant)
    {
        List<RoleAssignmentRow> selected = [];
        foreach (RoleAssignmentRow row in source)
        {
            if (!AccessScope.TryParse(row.ScopeValue, out AccessScope? scope))
            {
                relevant = [];
                return false;
            }

            if (IsRelated(scope, rootScope))
            {
                selected.Add(row);
            }
        }

        relevant = selected.ToArray();
        return true;
    }

    private static bool TrySelectRelevant(
        ProfileAssignmentRow[] source,
        AccessScope rootScope,
        out ProfileAssignmentRow[] relevant)
    {
        List<ProfileAssignmentRow> selected = [];
        foreach (ProfileAssignmentRow row in source)
        {
            if (!AccessScope.TryParse(
                    row.AssignmentScopeValue,
                    out AccessScope? scope))
            {
                relevant = [];
                return false;
            }

            if (IsRelated(scope, rootScope))
            {
                selected.Add(row);
            }
        }

        relevant = selected.ToArray();
        return true;
    }

    private static bool TryMapRoleGrants(
        RoleAssignmentRow[] assignments,
        RoleRow[] roles,
        RolePermissionRow[] permissions,
        out AccessControlAuthorityRoleGrant[] grants)
    {
        Dictionary<Guid, RoleRow> roleById = roles.ToDictionary(role => role.Id);
        List<AccessControlAuthorityRoleGrant> mapped = [];
        foreach (RoleAssignmentRow assignment in assignments)
        {
            if (!roleById.TryGetValue(assignment.RoleId, out RoleRow? role) ||
                !AccessScope.TryParse(
                    assignment.ScopeValue,
                    out AccessScope? scope))
            {
                grants = [];
                return false;
            }

            mapped.Add(new AccessControlAuthorityRoleGrant(
                assignment.AssignmentId,
                role.Id,
                role.Name,
                permissions
                    .Where(permission => permission.RoleId == role.Id)
                    .Select(permission => permission.PermissionCode)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                scope,
                assignment.ExpiresAtUnixMilliseconds is long expiry
                    ? DateTimeOffset.FromUnixTimeMilliseconds(expiry)
                    : null));
        }

        grants = mapped
            .OrderBy(grant => grant.Scope.Value, StringComparer.Ordinal)
            .ThenBy(grant => grant.RoleName, StringComparer.Ordinal)
            .ThenBy(grant => grant.RoleId)
            .ThenBy(grant => grant.AssignmentId)
            .ToArray();
        return true;
    }

    private static bool TryMapProfileGrants(
        ProfileAssignmentRow[] assignments,
        ProfileRow[] profiles,
        ProfilePermissionRow[] permissions,
        out AccessControlAuthorityProfileGrant[] grants)
    {
        Dictionary<Guid, ProfileRow> profileById = profiles
            .ToDictionary(profile => profile.Id);
        List<AccessControlAuthorityProfileGrant> mapped = [];
        foreach (ProfileAssignmentRow assignment in assignments)
        {
            if (!profileById.TryGetValue(
                    assignment.ProfileId,
                    out ProfileRow? profile) ||
                !AccessScope.TryParse(
                    assignment.AssignmentScopeValue,
                    out AccessScope? assignmentScope) ||
                !AccessScope.TryParse(
                    profile.OwnerScopeValue,
                    out AccessScope? ownerScope) ||
                !TryMapProfileStatus(profile.Status, out AccessProfileStatus profileStatus))
            {
                grants = [];
                return false;
            }

            mapped.Add(new AccessControlAuthorityProfileGrant(
                assignment.AssignmentId,
                assignmentScope,
                profile.Id,
                ownerScope,
                profile.Key,
                profileStatus,
                profile.Version,
                permissions
                    .Where(permission => permission.ProfileId == profile.Id)
                    .Select(permission => permission.PermissionCode)
                    .Order(StringComparer.Ordinal)
                    .ToArray()));
        }

        grants = mapped
            .OrderBy(grant => grant.AssignmentScope.Value, StringComparer.Ordinal)
            .ThenBy(grant => grant.OwnerScope.Value, StringComparer.Ordinal)
            .ThenBy(grant => grant.ProfileKey, StringComparer.Ordinal)
            .ThenBy(grant => grant.ProfileId)
            .ThenBy(grant => grant.AssignmentId)
            .ToArray();
        return true;
    }

    private static bool TryMapProfileStatus(int value, out AccessProfileStatus status)
    {
        status = value switch
        {
            (int)AccessProfileStatus.Active => AccessProfileStatus.Active,
            (int)AccessProfileStatus.Archived => AccessProfileStatus.Archived,
            _ => AccessProfileStatus.Unknown
        };
        return status is not AccessProfileStatus.Unknown;
    }

    private static bool IsRelated(AccessScope candidate, AccessScope root) =>
        candidate.IsGlobal ||
        root.IsGlobal ||
        IsPrefix(candidate, root) ||
        IsPrefix(root, candidate);

    private static bool IsPrefix(AccessScope prefix, AccessScope value)
    {
        if (prefix.Segments.Count > value.Segments.Count)
        {
            return false;
        }

        for (int index = 0; index < prefix.Segments.Count; index++)
        {
            if (prefix.Segments[index] != value.Segments[index])
            {
                return false;
            }
        }

        return true;
    }

    private static string Hash(
        AccessSubject subject,
        AccessScope rootScope,
        AccessScope[] expectedTargetScopes,
        long revision,
        AccessControlAuthorityRoleGrant[] roleGrants,
        AccessControlAuthorityProfileGrant[] profileGrants)
    {
        using MemoryStream canonical = new();
        Append(canonical, DigestDomain);
        Append(canonical, ContractVersion.ToString(CultureInfo.InvariantCulture));
        Append(canonical, ((int)subject.Kind).ToString(CultureInfo.InvariantCulture));
        Append(canonical, subject.Id);
        Append(canonical, rootScope.Value);
        Append(canonical, expectedTargetScopes.Length.ToString(
            CultureInfo.InvariantCulture));
        foreach (AccessScope expectedTargetScope in expectedTargetScopes)
        {
            Append(canonical, expectedTargetScope.Value);
        }

        Append(canonical, revision.ToString(CultureInfo.InvariantCulture));
        Append(canonical, roleGrants.Length.ToString(CultureInfo.InvariantCulture));
        foreach (AccessControlAuthorityRoleGrant grant in roleGrants)
        {
            Append(canonical, grant.AssignmentId.ToString("D"));
            Append(canonical, grant.RoleId.ToString("D"));
            Append(canonical, grant.RoleName);
            Append(canonical, grant.Scope.Value);
            Append(canonical, grant.ExpiresAtUtc?.ToUnixTimeMilliseconds().ToString(
                CultureInfo.InvariantCulture) ?? string.Empty);
            Append(canonical, grant.Permissions.Count.ToString(CultureInfo.InvariantCulture));
            foreach (string permission in grant.Permissions)
            {
                Append(canonical, permission);
            }
        }

        Append(canonical, profileGrants.Length.ToString(CultureInfo.InvariantCulture));
        foreach (AccessControlAuthorityProfileGrant grant in profileGrants)
        {
            Append(canonical, grant.AssignmentId.ToString("D"));
            Append(canonical, grant.AssignmentScope.Value);
            Append(canonical, grant.ProfileId.ToString("D"));
            Append(canonical, grant.OwnerScope.Value);
            Append(canonical, grant.ProfileKey);
            Append(canonical, ((int)grant.ProfileStatus).ToString(CultureInfo.InvariantCulture));
            Append(canonical, grant.ProfileVersion.ToString(CultureInfo.InvariantCulture));
            Append(canonical, grant.Permissions.Count.ToString(CultureInfo.InvariantCulture));
            foreach (string permission in grant.Permissions)
            {
                Append(canonical, permission);
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(canonical.ToArray()));
    }

    private static void Append(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    private static AccessControlSubjectAuthoritySnapshot Empty(
        AccessControlAuthoritySnapshotStatus status,
        DateTimeOffset observedAtUtc,
        long? revision = null,
        AccessSubject? subject = null,
        AccessScope? rootScope = null,
        IReadOnlyList<AccessScope>? expectedTargetScopes = null) => new(
        status,
        ContractVersion,
        subject?.Kind ?? AccessSubjectKind.Unknown,
        subject?.Id,
        rootScope,
        expectedTargetScopes ?? [],
        revision,
        observedAtUtc,
        null,
        null,
        [],
        []);

    private sealed record RoleAssignmentRow(
        Guid AssignmentId,
        Guid RoleId,
        string ScopeValue,
        long? ExpiresAtUnixMilliseconds);

    private sealed record RoleRow(Guid Id, string Name);

    private sealed record RolePermissionRow(
        Guid RoleId,
        string PermissionCode);

    private sealed record ProfileAssignmentRow(
        Guid AssignmentId,
        Guid ProfileId,
        string AssignmentScopeValue);

    private sealed record ProfileRow(
        Guid Id,
        string OwnerScopeValue,
        string Key,
        int Status,
        long Version);

    private sealed record ProfilePermissionRow(
        Guid ProfileId,
        string PermissionCode);
}
