namespace Gma.Modules.AccessControl.Persistence;

using System.Diagnostics.CodeAnalysis;
using Gma.Framework.Messaging;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using DomainScopeDestroyReceipt =
    Gma.Modules.AccessControl.Persistence.Entities.AccessControlScopeDestroyReceipt;

internal sealed partial class AccessControlScopeLifecycleService(
    AccessControlDbContext dbContext,
    ISystemClock clock)
    : IAccessControlScopeLifecycle
{
    private const string IdCursorPrefix = "id:";

    public async Task<AccessControlScopeSnapshot> GetSnapshotAsync(
        AccessControlScopeCoordinate coordinate,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(coordinate, out NormalizedCoordinate? normalized))
        {
            return new AccessControlScopeSnapshot(
                AccessControlScopeStatus.Invalid,
                0);
        }

        return await this.ReadSnapshotAsync(normalized, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AccessControlScopeExportPage> ExportAsync(
        AccessControlScopeExportRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            !TryNormalize(request.Coordinate, out NormalizedCoordinate? coordinate) ||
            request.ExpectedRevision < 0 ||
            request.ExpectedRevision == long.MaxValue ||
            request.PageSize is < 1 or >
                AccessControlScopeLifecycleLimits.MaximumPageSize ||
            request.Store is <= AccessControlScopeExportStore.Unknown or >
                AccessControlScopeExportStore.ProfileChanges ||
            (request.AfterCursor is not null &&
             (request.AfterCursor.Length == 0 ||
              request.AfterCursor.Length >
                AccessControlScopeLifecycleLimits.MaximumCursorLength ||
              request.AfterCursor.Any(char.IsControl))))
        {
            return EmptyPage(
                AccessControlScopeExportStatus.Invalid,
                0,
                request?.Store ?? AccessControlScopeExportStore.Unknown);
        }

        AccessControlScopeSnapshot snapshot = await this.ReadSnapshotAsync(
                coordinate,
                cancellationToken)
            .ConfigureAwait(false);
        if (snapshot.Status == AccessControlScopeStatus.Invalid)
        {
            return EmptyPage(
                AccessControlScopeExportStatus.Invalid,
                snapshot.Revision,
                request.Store);
        }

        if (snapshot.Status == AccessControlScopeStatus.Missing)
        {
            return EmptyPage(
                request.ExpectedRevision == 0
                    ? AccessControlScopeExportStatus.Missing
                    : AccessControlScopeExportStatus.Stale,
                0,
                request.Store);
        }

        if (snapshot.Status == AccessControlScopeStatus.Closed)
        {
            return EmptyPage(
                AccessControlScopeExportStatus.Closed,
                snapshot.Revision,
                request.Store);
        }

        if (snapshot.Revision != request.ExpectedRevision)
        {
            return EmptyPage(
                AccessControlScopeExportStatus.Stale,
                snapshot.Revision,
                request.Store);
        }

        if (!TryParseIdCursor(request.AfterCursor, out Guid? afterId))
        {
            return EmptyPage(
                AccessControlScopeExportStatus.Invalid,
                snapshot.Revision,
                request.Store);
        }

        return request.Store switch
        {
            AccessControlScopeExportStore.RoleAssignments =>
                await this.ExportRoleAssignmentsAsync(
                    request,
                    coordinate,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            AccessControlScopeExportStore.Profiles =>
                await this.ExportProfilesAsync(
                    request,
                    coordinate,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            AccessControlScopeExportStore.ProfileAssignments =>
                await this.ExportProfileAssignmentsAsync(
                    request,
                    coordinate,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            AccessControlScopeExportStore.ProfileChanges =>
                await this.ExportProfileChangesAsync(
                    request,
                    coordinate,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            _ => EmptyPage(
                AccessControlScopeExportStatus.Invalid,
                snapshot.Revision,
                request.Store)
        };
    }

    private async Task<AccessControlScopeExportPage>
        ExportRoleAssignmentsAsync(
            AccessControlScopeExportRequest request,
            NormalizedCoordinate coordinate,
            Guid? afterId,
            CancellationToken cancellationToken)
    {
        string root = coordinate.ScopeValue;
        string prefix = root + "/";
        IQueryable<AccessSubjectRoleAssignment> query = dbContext
            .SubjectRoleAssignments
            .Include(assignment => assignment.Role!)
                .ThenInclude(role => role.Permissions)
            .AsNoTracking()
            .Where(assignment =>
                assignment.ScopeValue == root ||
                assignment.ScopeValue.StartsWith(prefix));
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(assignment =>
                assignment.Id.CompareTo(cursor) > 0);
        }

        AccessSubjectRoleAssignment[] loaded = await query
            .OrderBy(assignment => assignment.Id)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                request,
                coordinate,
                loaded,
                assignment => assignment.Id,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AccessControlScopeExportPage> ExportProfilesAsync(
        AccessControlScopeExportRequest request,
        NormalizedCoordinate coordinate,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        string root = coordinate.ScopeValue;
        string prefix = root + "/";
        IQueryable<AccessProfile> query = dbContext.AccessProfiles
            .Include(profile => profile.Permissions)
            .AsNoTracking()
            .Where(profile =>
                profile.OwnerScopeValue == root ||
                profile.OwnerScopeValue.StartsWith(prefix));
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(profile => profile.Id.CompareTo(cursor) > 0);
        }

        AccessProfile[] loaded = await query
            .OrderBy(profile => profile.Id)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                request,
                coordinate,
                loaded,
                profile => profile.Id,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AccessControlScopeExportPage>
        ExportProfileAssignmentsAsync(
            AccessControlScopeExportRequest request,
            NormalizedCoordinate coordinate,
            Guid? afterId,
            CancellationToken cancellationToken)
    {
        string root = coordinate.ScopeValue;
        string prefix = root + "/";
        IQueryable<AccessProfileAssignment> query = dbContext
            .AccessProfileAssignments
            .Include(assignment => assignment.Profile)
            .AsNoTracking()
            .Where(assignment =>
                assignment.Profile!.OwnerScopeValue == root ||
                assignment.Profile.OwnerScopeValue.StartsWith(prefix) ||
                assignment.AssignmentScopeValue == root ||
                assignment.AssignmentScopeValue.StartsWith(prefix));
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(assignment =>
                assignment.Id.CompareTo(cursor) > 0);
        }

        AccessProfileAssignment[] loaded = await query
            .OrderBy(assignment => assignment.Id)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                request,
                coordinate,
                loaded,
                assignment => assignment.Id,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AccessControlScopeExportPage>
        ExportProfileChangesAsync(
            AccessControlScopeExportRequest request,
            NormalizedCoordinate coordinate,
            Guid? afterId,
            CancellationToken cancellationToken)
    {
        string root = coordinate.ScopeValue;
        string prefix = root + "/";
        IQueryable<AccessProfileChange> query = dbContext
            .AccessProfileChanges
            .Include(change => change.Profile)
            .AsNoTracking()
            .Where(change =>
                change.Profile!.OwnerScopeValue == root ||
                change.Profile.OwnerScopeValue.StartsWith(prefix) ||
                change.AssignmentScopeValue == root ||
                (change.AssignmentScopeValue != null &&
                 change.AssignmentScopeValue.StartsWith(prefix)));
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(change => change.Id.CompareTo(cursor) > 0);
        }

        AccessProfileChange[] loaded = await query
            .OrderBy(change => change.Id)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                request,
                coordinate,
                loaded,
                change => change.Id,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AccessControlScopeExportPage> CompleteGuidPageAsync<T>(
        AccessControlScopeExportRequest request,
        NormalizedCoordinate coordinate,
        IReadOnlyList<T> loaded,
        Func<T, Guid> id,
        Func<T, AccessControlScopeExportRecord> map,
        CancellationToken cancellationToken)
    {
        bool hasMore = loaded.Count > request.PageSize;
        T[] selected = loaded.Take(request.PageSize).ToArray();
        string? nextCursor = selected.Length == 0
            ? request.AfterCursor
            : IdCursor(id(selected[^1]));
        AccessControlScopeSnapshot current = await this.ReadExportFenceAsync(
                coordinate,
                cancellationToken)
            .ConfigureAwait(false);
        if (current.Status == AccessControlScopeStatus.Invalid)
        {
            return EmptyPage(
                AccessControlScopeExportStatus.Invalid,
                current.Revision,
                request.Store);
        }

        if (current.Status == AccessControlScopeStatus.Closed)
        {
            return EmptyPage(
                AccessControlScopeExportStatus.Closed,
                current.Revision,
                request.Store);
        }

        if (current.Status != AccessControlScopeStatus.Open ||
            current.Revision != request.ExpectedRevision)
        {
            return EmptyPage(
                AccessControlScopeExportStatus.Stale,
                current.Revision,
                request.Store);
        }

        return new AccessControlScopeExportPage(
            AccessControlScopeExportStatus.Completed,
            current.Revision,
            request.Store,
            selected.Select(map).ToArray(),
            nextCursor,
            hasMore);
    }

    private async Task<AccessControlScopeSnapshot> ReadSnapshotAsync(
        NormalizedCoordinate coordinate,
        CancellationToken cancellationToken)
    {
        StateLookup state = await this.ReadStateAsync(
                coordinate,
                cancellationToken)
            .ConfigureAwait(false);
        if (state.IsCoordinateConflict)
        {
            return new AccessControlScopeSnapshot(
                AccessControlScopeStatus.Invalid,
                0);
        }

        if (state.State is { IsClosed: true } closed)
        {
            return await this.ReadClosedSnapshotAsync(
                    closed,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!await this.HasScopeDataAsync(coordinate, cancellationToken)
                .ConfigureAwait(false))
        {
            return new AccessControlScopeSnapshot(
                AccessControlScopeStatus.Missing,
                0);
        }

        long revision = await AccessControlManagementLock.ReadRevisionAsync(
                dbContext,
                cancellationToken)
            .ConfigureAwait(false);
        return new AccessControlScopeSnapshot(
            AccessControlScopeStatus.Open,
            revision);
    }

    private async Task<AccessControlScopeSnapshot> ReadExportFenceAsync(
        NormalizedCoordinate coordinate,
        CancellationToken cancellationToken)
    {
        StateLookup state = await this.ReadStateAsync(
                coordinate,
                cancellationToken)
            .ConfigureAwait(false);
        if (state.IsCoordinateConflict)
        {
            return new AccessControlScopeSnapshot(
                AccessControlScopeStatus.Invalid,
                0);
        }

        if (state.State is { IsClosed: true } closed)
        {
            return new AccessControlScopeSnapshot(
                AccessControlScopeStatus.Closed,
                closed.CloseRevision);
        }

        long revision = await AccessControlManagementLock.ReadRevisionAsync(
                dbContext,
                cancellationToken)
            .ConfigureAwait(false);
        return new AccessControlScopeSnapshot(
            AccessControlScopeStatus.Open,
            revision);
    }

    private async Task<AccessControlScopeSnapshot> ReadClosedSnapshotAsync(
        AccessControlScopeState state,
        CancellationToken cancellationToken)
    {
        if (!state.CloseOperationId.HasValue)
        {
            return new AccessControlScopeSnapshot(
                AccessControlScopeStatus.Invalid,
                0);
        }

        Guid operationId = state.CloseOperationId.Value;
        AccessControlScopeDestroyOperation? operation = await dbContext
            .AccessScopeDestroyOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.OperationId == operationId,
                cancellationToken)
            .ConfigureAwait(false);
        DomainScopeDestroyReceipt? receipt = await dbContext
            .AccessScopeDestroyReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.OperationId == operationId,
                cancellationToken)
            .ConfigureAwait(false);
        if ((operation is null) == (receipt is null))
        {
            return new AccessControlScopeSnapshot(
                AccessControlScopeStatus.Invalid,
                0);
        }

        long selectedRevision;
        string scopeHash;
        string scopeValue;
        string transportScopeId;
        long resultingRevision;
        if (operation is not null)
        {
            selectedRevision = operation.ExpectedRevision;
            scopeHash = operation.ScopeHash;
            scopeValue = operation.ScopeValue;
            transportScopeId = operation.TransportScopeId;
            resultingRevision = operation.ResultingRevision;
        }
        else
        {
            selectedRevision = receipt!.ExpectedRevision;
            scopeHash = receipt.ScopeHash;
            scopeValue = receipt.ScopeValue;
            transportScopeId = receipt.TransportScopeId;
            resultingRevision = receipt.ResultingRevision;
        }

        if (selectedRevision < 0 ||
            selectedRevision >= state.CloseRevision ||
            resultingRevision != state.CloseRevision ||
            !string.Equals(
                scopeHash,
                state.ScopeHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                scopeValue,
                state.ScopeValue,
                StringComparison.Ordinal) ||
            !string.Equals(
                transportScopeId,
                state.TransportScopeId,
                StringComparison.Ordinal))
        {
            return new AccessControlScopeSnapshot(
                AccessControlScopeStatus.Invalid,
                0);
        }

        return new AccessControlScopeSnapshot(
            AccessControlScopeStatus.Closed,
            state.CloseRevision)
        {
            SelectedRevision = selectedRevision
        };
    }

    private async Task<StateLookup> ReadStateAsync(
        NormalizedCoordinate coordinate,
        CancellationToken cancellationToken)
    {
        AccessControlScopeState[] matches = await dbContext.AccessScopeStates
            .AsNoTracking()
            .Where(state =>
                state.ScopeHash == coordinate.ScopeHash ||
                state.TransportScopeId == coordinate.TransportScopeId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        AccessControlScopeState? exact = matches.SingleOrDefault(state =>
            string.Equals(
                state.ScopeHash,
                coordinate.ScopeHash,
                StringComparison.Ordinal) &&
            string.Equals(
                state.ScopeValue,
                coordinate.ScopeValue,
                StringComparison.Ordinal) &&
            string.Equals(
                state.TransportScopeId,
                coordinate.TransportScopeId,
                StringComparison.Ordinal));
        return exact is not null && matches.Length == 1
            ? new StateLookup(exact, IsCoordinateConflict: false)
            : matches.Length == 0
                ? new StateLookup(null, IsCoordinateConflict: false)
                : new StateLookup(null, IsCoordinateConflict: true);
    }

    private async Task<bool> HasScopeDataAsync(
        NormalizedCoordinate coordinate,
        CancellationToken cancellationToken)
    {
        string root = coordinate.ScopeValue;
        string prefix = root + "/";
        IQueryable<int> candidates = dbContext.SubjectRoleAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.ScopeValue == root ||
                assignment.ScopeValue.StartsWith(prefix))
            .Select(_ => 1)
            .Concat(dbContext.AccessProfiles
                .AsNoTracking()
                .Where(profile =>
                    profile.OwnerScopeValue == root ||
                    profile.OwnerScopeValue.StartsWith(prefix))
                .Select(_ => 1))
            .Concat(dbContext.AccessProfileAssignments
                .AsNoTracking()
                .Where(assignment =>
                    assignment.AssignmentScopeValue == root ||
                    assignment.AssignmentScopeValue.StartsWith(prefix))
                .Select(_ => 1))
            .Concat(dbContext.AccessProfileChanges
                .AsNoTracking()
                .Where(change =>
                    change.AssignmentScopeValue == root ||
                    (change.AssignmentScopeValue != null &&
                     change.AssignmentScopeValue.StartsWith(prefix)))
                .Select(_ => 1))
            .Concat(dbContext.InboxMessages
                .AsNoTracking()
                .Where(message =>
                    message.ScopeId == coordinate.TransportScopeId)
                .Select(_ => 1));
        return await candidates.AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool TryNormalize(
        AccessControlScopeCoordinate? coordinate,
        [NotNullWhen(true)]
        out NormalizedCoordinate? normalized)
    {
        normalized = null;
        if (coordinate?.RootScope is null || coordinate.RootScope.IsGlobal)
        {
            return false;
        }

        string? transportScopeId;
        try
        {
            transportScopeId = MessageScopeIds.NormalizeOptional(
                coordinate.TransportScopeId,
                nameof(coordinate.TransportScopeId));
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (transportScopeId is null)
        {
            return false;
        }

        normalized = new NormalizedCoordinate(
            coordinate.RootScope.Value,
            AccessScopeIndex.Create(coordinate.RootScope.Value),
            transportScopeId);
        return true;
    }

    private static bool TryParseIdCursor(
        string? cursor,
        out Guid? afterId)
    {
        afterId = null;
        if (cursor is null)
        {
            return true;
        }

        if (!cursor.StartsWith(IdCursorPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(
                cursor[IdCursorPrefix.Length..],
                "D",
                out Guid parsed) ||
            parsed == Guid.Empty)
        {
            return false;
        }

        afterId = parsed;
        return true;
    }

    private static string IdCursor(Guid id) =>
        IdCursorPrefix + id.ToString("D");

    private static AccessControlScopeExportPage EmptyPage(
        AccessControlScopeExportStatus status,
        long revision,
        AccessControlScopeExportStore store) =>
        new(status, revision, store, [], null, false);

    private sealed record NormalizedCoordinate(
        string ScopeValue,
        string ScopeHash,
        string TransportScopeId);

    private sealed record StateLookup(
        AccessControlScopeState? State,
        bool IsCoordinateConflict);
}
