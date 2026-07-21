namespace Gma.Modules.AccessControl.Persistence.Repositories;

using Gma.Framework.AccessControl;
using Gma.Framework.Pagination;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using DomainChangeKind = Gma.Modules.AccessControl.Domain.Enums.AccessProfileChangeKind;
using DomainStatus = Gma.Modules.AccessControl.Domain.Enums.AccessProfileStatus;

internal sealed class AccessProfileRepository(AccessControlDbContext dbContext) : IAccessProfileRepository
{
    public Task<bool> KeyExistsAsync(
        AccessScope ownerScope,
        string key,
        CancellationToken cancellationToken) =>
        dbContext.AccessProfiles.AnyAsync(
            profile => profile.OwnerScopeValue == ownerScope.Value && profile.Key == key,
            cancellationToken);

    public async Task<AccessProfile?> GetAsync(
        Guid profileId,
        AccessScope ownerScope,
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<AccessProfile> query = dbContext.AccessProfiles.Include(profile => profile.Permissions);
        if (!tracking) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(
            profile => profile.Id == profileId && profile.OwnerScopeValue == ownerScope.Value,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccessProfileDetails?> GetDetailsAsync(
        Guid profileId,
        AccessScope ownerScope,
        CancellationToken cancellationToken)
    {
        AccessProfileDetailsProjection? profile = await this.ProjectDetails(dbContext.AccessProfiles.AsNoTracking()
                .Where(profile => profile.Id == profileId && profile.OwnerScopeValue == ownerScope.Value))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return profile is null ? null : ToDetails(profile);
    }

    public async Task<AccessProfileDetails?> GetDetailsByKeyAsync(
        AccessScope ownerScope,
        string key,
        CancellationToken cancellationToken)
    {
        AccessProfileDetailsProjection? profile = await this.ProjectDetails(dbContext.AccessProfiles.AsNoTracking()
                .Where(profile => profile.OwnerScopeValue == ownerScope.Value && profile.Key == key))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return profile is null ? null : ToDetails(profile);
    }

    public async Task<IReadOnlyList<AccessProfileDetails>> ListDetailsForSubjectAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        CancellationToken cancellationToken)
    {
        int subjectKind = (int)subject.Kind;
        string subjectId = subject.Id;
        string ownerScopeValue = ownerScope.Value;
        IQueryable<AccessProfile> assignedProfiles =
            from profile in dbContext.AccessProfiles.AsNoTracking()
            join assignment in dbContext.AccessProfileAssignments.AsNoTracking()
                on profile.Id equals assignment.ProfileId
            where profile.OwnerScopeValue == ownerScopeValue &&
                  assignment.SubjectKind == subjectKind &&
                  assignment.SubjectId == subjectId &&
                  assignment.AssignmentScopeValue == ownerScopeValue
            select profile;

        AccessProfileDetailsProjection[] profiles = await this.ProjectDetails(assignedProfiles
                .OrderBy(profile => profile.Key)
                .ThenBy(profile => profile.Id))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return profiles.Select(ToDetails).ToArray();
    }

    public async Task<IReadOnlyList<ScopedAccessProfileAssignmentDetails>> ListScopedDetailsForSubjectAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        CancellationToken cancellationToken)
    {
        int subjectKind = (int)subject.Kind;
        string subjectId = subject.Id;
        string ownerScopeValue = ownerScope.Value;
        ScopedAccessProfileAssignmentProjection[] persisted = await dbContext.AccessProfileAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.SubjectKind == subjectKind &&
                assignment.SubjectId == subjectId &&
                assignment.Profile != null &&
                assignment.Profile.OwnerScopeValue == ownerScopeValue)
            .OrderBy(assignment => assignment.AssignmentScopeValue)
            .ThenBy(assignment => assignment.Profile!.Key)
            .ThenBy(assignment => assignment.ProfileId)
            .Select(assignment => new ScopedAccessProfileAssignmentProjection(
                assignment.ProfileId,
                assignment.Profile!.OwnerScopeValue,
                assignment.Profile.Key,
                assignment.Profile.DisplayName,
                assignment.Profile.Description,
                assignment.Profile.Status,
                assignment.Profile.Version,
                assignment.Profile.Permissions.OrderBy(permission => permission.PermissionCode)
                    .Select(permission => permission.PermissionCode).ToArray(),
                dbContext.AccessProfileAssignments.Count(candidate => candidate.ProfileId == assignment.ProfileId),
                assignment.Profile.CreatedAtUtc,
                assignment.Profile.LastChangedAtUtc,
                assignment.AssignmentScopeValue))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return persisted.Select(assignment => new ScopedAccessProfileAssignmentDetails(
            ToDetails(new AccessProfileDetailsProjection(
                assignment.ProfileId,
                assignment.OwnerScopeValue,
                assignment.Key,
                assignment.DisplayName,
                assignment.Description,
                assignment.Status,
                assignment.Version,
                assignment.Permissions,
                assignment.AssignmentCount,
                assignment.CreatedAtUtc,
                assignment.LastChangedAtUtc)),
            AccessScope.Parse(assignment.AssignmentScopeValue))).ToArray();
    }

    public async Task<IReadOnlyList<AccessProfile>> ListTrackedAsync(
        IReadOnlyCollection<Guid> profileIds,
        AccessScope ownerScope,
        CancellationToken cancellationToken)
    {
        return await dbContext.AccessProfiles
            .Include(profile => profile.Permissions)
            .Where(profile => profile.OwnerScopeValue == ownerScope.Value && profileIds.Contains(profile.Id))
            .OrderBy(profile => profile.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<int> CountAssignmentsAsync(Guid profileId, CancellationToken cancellationToken) =>
        dbContext.AccessProfileAssignments.CountAsync(
            assignment => assignment.ProfileId == profileId,
            cancellationToken);

    public void Add(AccessProfile profile) => dbContext.AccessProfiles.Add(profile);

    public Task<bool> AssignmentExistsAsync(
        Guid profileId,
        AccessSubject subject,
        AccessScope assignmentScope,
        CancellationToken cancellationToken) =>
        dbContext.AccessProfileAssignments.AnyAsync(
            assignment => assignment.ProfileId == profileId &&
                          assignment.SubjectKind == (int)subject.Kind &&
                          assignment.SubjectId == subject.Id &&
                          assignment.AssignmentScopeValue == assignmentScope.Value,
            cancellationToken);

    public Task<AccessProfileAssignment?> GetAssignmentAsync(
        Guid profileId,
        AccessSubject subject,
        AccessScope assignmentScope,
        CancellationToken cancellationToken) =>
        dbContext.AccessProfileAssignments.SingleOrDefaultAsync(
            assignment => assignment.ProfileId == profileId &&
                          assignment.SubjectKind == (int)subject.Kind &&
                          assignment.SubjectId == subject.Id &&
                          assignment.AssignmentScopeValue == assignmentScope.Value,
            cancellationToken);

    public async Task<IReadOnlyList<AccessProfileAssignment>> ListTrackedAssignmentsAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        AccessScope? assignmentScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(ownerScope);
        IQueryable<AccessProfileAssignment> assignments = dbContext.AccessProfileAssignments
            .Include(assignment => assignment.Profile)
            .Where(assignment =>
                assignment.SubjectKind == (int)subject.Kind &&
                assignment.SubjectId == subject.Id &&
                assignment.Profile != null &&
                assignment.Profile.OwnerScopeValue == ownerScope.Value);
        if (assignmentScope is not null)
        {
            assignments = assignments.Where(assignment =>
                assignment.AssignmentScopeValue == assignmentScope.Value);
        }

        return await assignments
            .OrderBy(assignment => assignment.ProfileId)
            .ThenBy(assignment => assignment.AssignmentScopeValue)
            .ThenBy(assignment => assignment.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void AddAssignment(AccessProfileAssignment assignment) =>
        dbContext.AccessProfileAssignments.Add(assignment);

    public void RemoveAssignment(AccessProfileAssignment assignment) =>
        dbContext.AccessProfileAssignments.Remove(assignment);

    public async Task<AccessControlPage<AccessProfileDetails>> ListAsync(
        AccessScope ownerScope,
        bool includeArchived,
        PageRequest pageRequest,
        CancellationToken cancellationToken)
    {
        IQueryable<AccessProfile> profiles = dbContext.AccessProfiles
            .AsNoTracking()
            .Where(profile => profile.OwnerScopeValue == ownerScope.Value);
        if (!includeArchived)
        {
            profiles = profiles.Where(profile => profile.Status == DomainStatus.Active);
        }

        IQueryable<AccessProfile> page = profiles
            .OrderBy(profile => profile.Key)
            .ThenBy(profile => profile.Id)
            .Skip(pageRequest.SkipCount)
            .Take(pageRequest.PageSize + 1);
        AccessProfileDetailsProjection[] persisted = await this.ProjectDetails(page)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        AccessProfileDetails[] rows = persisted.Select(ToDetails).ToArray();
        return Page(rows, pageRequest);
    }

    public async Task<AccessControlPage<AccessProfileAssignmentDetails>> ListAssignmentsAsync(
        Guid profileId,
        AccessScope ownerScope,
        PageRequest pageRequest,
        CancellationToken cancellationToken)
    {
        AccessProfileAssignmentProjection[] persisted = await dbContext.AccessProfileAssignments
            .AsNoTracking()
            .Where(assignment => assignment.ProfileId == profileId &&
                                 assignment.Profile != null &&
                                 assignment.Profile.OwnerScopeValue == ownerScope.Value)
            .OrderBy(assignment => assignment.SubjectKind)
            .ThenBy(assignment => assignment.SubjectId)
            .ThenBy(assignment => assignment.AssignmentScopeValue)
            .ThenBy(assignment => assignment.Id)
            .Skip(pageRequest.SkipCount)
            .Take(pageRequest.PageSize + 1)
            .Select(assignment => new AccessProfileAssignmentProjection(
                assignment.Id, assignment.ProfileId, assignment.SubjectKind, assignment.SubjectId,
                assignment.CreatedByKind, assignment.CreatedById, assignment.CreatedAtUtc,
                assignment.AssignmentScopeValue))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        AccessProfileAssignmentDetails[] rows = persisted.Select(assignment => new AccessProfileAssignmentDetails(
            assignment.Id, assignment.ProfileId, ToSubjectKind(assignment.SubjectKind), assignment.SubjectId,
            ToSubjectKind(assignment.CreatedByKind), assignment.CreatedById, assignment.CreatedAtUtc,
            AccessScope.Parse(assignment.AssignmentScopeValue))).ToArray();
        return Page(rows, pageRequest);
    }

    public async Task<AccessControlPage<AccessProfileChangeDetails>> ListChangesAsync(
        Guid profileId,
        AccessScope ownerScope,
        PageRequest pageRequest,
        CancellationToken cancellationToken)
    {
        AccessProfileChangeProjection[] persisted = await dbContext.AccessProfileChanges
            .AsNoTracking()
            .Where(change => change.ProfileId == profileId &&
                             change.Profile != null &&
                             change.Profile.OwnerScopeValue == ownerScope.Value)
            .OrderByDescending(change => change.OccurredAtUtc)
            .ThenByDescending(change => change.Id)
            .Skip(pageRequest.SkipCount)
            .Take(pageRequest.PageSize + 1)
            .Select(change => new AccessProfileChangeProjection(
                change.Id, change.ProfileId, change.Kind, change.ActorKind, change.ActorId,
                change.SubjectKind, change.SubjectId, change.ProfileVersion, change.OccurredAtUtc,
                change.AssignmentScopeValue))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        AccessProfileChangeDetails[] rows = persisted.Select(change => new AccessProfileChangeDetails(
            change.Id, change.ProfileId, ToContract(change.Kind),
            ToSubjectKind(change.ActorKind), change.ActorId,
            change.SubjectKind.HasValue ? ToSubjectKind(change.SubjectKind.Value) : null,
            change.SubjectId, change.ProfileVersion, change.OccurredAtUtc,
            change.AssignmentScopeValue is null ? null : AccessScope.Parse(change.AssignmentScopeValue))).ToArray();
        return Page(rows, pageRequest);
    }

    private IQueryable<AccessProfileDetailsProjection> ProjectDetails(IQueryable<AccessProfile> profiles) =>
        profiles.Select(profile => new AccessProfileDetailsProjection(
            profile.Id,
            profile.OwnerScopeValue,
            profile.Key,
            profile.DisplayName,
            profile.Description,
            profile.Status,
            profile.Version,
            profile.Permissions.OrderBy(permission => permission.PermissionCode)
                .Select(permission => permission.PermissionCode).ToArray(),
            dbContext.AccessProfileAssignments.Count(assignment => assignment.ProfileId == profile.Id),
            profile.CreatedAtUtc,
            profile.LastChangedAtUtc));

    private static AccessProfileDetails ToDetails(AccessProfileDetailsProjection profile) =>
        new(
            profile.Id,
            AccessScope.Parse(profile.OwnerScopeValue),
            profile.Key,
            profile.DisplayName,
            profile.Description,
            ToContract(profile.Status),
            profile.Version,
            profile.Permissions,
            profile.AssignmentCount,
            profile.CreatedAtUtc,
            profile.LastChangedAtUtc);

    private static AccessControlPage<T> Page<T>(T[] rows, PageRequest request) =>
        new(rows.Take(request.PageSize).ToArray(), request.Page, request.PageSize, rows.Length > request.PageSize);

    private static AccessSubjectKind ToSubjectKind(int value)
    {
        AccessSubjectKind kind = (AccessSubjectKind)value;
        return kind != AccessSubjectKind.Unknown && Enum.IsDefined(kind)
            ? kind
            : throw new InvalidOperationException($"Persisted access subject kind '{value}' is invalid.");
    }

    private static AccessProfileStatus ToContract(DomainStatus status) =>
        status switch
        {
            DomainStatus.Active => AccessProfileStatus.Active,
            DomainStatus.Archived => AccessProfileStatus.Archived,
            _ => throw new InvalidOperationException($"Access-profile status '{status}' is invalid.")
        };

    private static AccessProfileChangeKind ToContract(DomainChangeKind kind) =>
        kind switch
        {
            DomainChangeKind.Created => AccessProfileChangeKind.Created,
            DomainChangeKind.Updated => AccessProfileChangeKind.Updated,
            DomainChangeKind.Archived => AccessProfileChangeKind.Archived,
            DomainChangeKind.Assigned => AccessProfileChangeKind.Assigned,
            DomainChangeKind.Unassigned => AccessProfileChangeKind.Unassigned,
            _ => throw new InvalidOperationException($"Access-profile change kind '{kind}' is invalid.")
        };

    private sealed record AccessProfileAssignmentProjection(
        Guid Id, Guid ProfileId, int SubjectKind, string SubjectId,
        int CreatedByKind, string CreatedById, DateTimeOffset CreatedAtUtc,
        string AssignmentScopeValue);

    private sealed record ScopedAccessProfileAssignmentProjection(
        Guid ProfileId,
        string OwnerScopeValue,
        string Key,
        string DisplayName,
        string Description,
        DomainStatus Status,
        long Version,
        string[] Permissions,
        int AssignmentCount,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset LastChangedAtUtc,
        string AssignmentScopeValue);

    private sealed record AccessProfileDetailsProjection(
        Guid Id,
        string OwnerScopeValue,
        string Key,
        string DisplayName,
        string Description,
        DomainStatus Status,
        long Version,
        string[] Permissions,
        int AssignmentCount,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset LastChangedAtUtc);

    private sealed record AccessProfileChangeProjection(
        Guid Id, Guid ProfileId, DomainChangeKind Kind, int ActorKind, string ActorId,
        int? SubjectKind, string? SubjectId, long ProfileVersion, DateTimeOffset OccurredAtUtc,
        string? AssignmentScopeValue);
}
