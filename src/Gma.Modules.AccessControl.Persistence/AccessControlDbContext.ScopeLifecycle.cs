namespace Gma.Modules.AccessControl.Persistence;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

public sealed partial class AccessControlDbContext
{
    private const int ScopeStateQueryBatchSize = 500;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.EnsureTrackedScopeWritesAdmitted();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        await this.EnsureTrackedScopeWritesAdmittedAsync(cancellationToken)
            .ConfigureAwait(false);
        return await base.SaveChangesAsync(
                acceptAllChangesOnSuccess,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<bool> IsScopeClosedAsync(
        string scopeValue,
        CancellationToken cancellationToken) =>
        !await this.AreScopesOpenAsync([scopeValue], cancellationToken)
            .ConfigureAwait(false);

    internal async Task<bool> AreScopesOpenAsync(
        IEnumerable<string> scopeValues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeValues);
        string[] candidateValues = scopeValues
            .SelectMany(ClosedStateCandidateValues)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidateValues.Length == 0)
        {
            return true;
        }

        string[] candidateHashes = candidateValues
            .Select(AccessScopeIndex.Create)
            .ToArray();
        AccessControlScopeState[] local = this.AccessScopeStates.Local
            .Where(state => candidateHashes.Contains(state.ScopeHash))
            .ToArray();
        HashSet<string> localHashes = local
            .Select(state => state.ScopeHash)
            .ToHashSet(StringComparer.Ordinal);
        List<AccessControlScopeState> states = [.. local];
        foreach (string[] batch in candidateHashes
                     .Where(hash => !localHashes.Contains(hash))
                     .Chunk(ScopeStateQueryBatchSize))
        {
            states.AddRange(await this.AccessScopeStates
                .AsNoTracking()
                .Where(state => batch.Contains(state.ScopeHash))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false));
        }

        EnsureNoHashCollision(states, candidateValues);
        return !states.Any(state =>
            state.IsClosed && candidateValues.Contains(
                state.ScopeValue,
                StringComparer.Ordinal));
    }

    internal async Task<bool> IsTransportScopeClosedAsync(
        string transportScopeId,
        CancellationToken cancellationToken)
    {
        if (this.AccessScopeStates.Local.Any(state =>
                state.TransportScopeId == transportScopeId &&
                state.IsClosed))
        {
            return true;
        }

        return await this.AccessScopeStates
            .AsNoTracking()
            .AnyAsync(
                state => state.TransportScopeId == transportScopeId &&
                    state.IsClosed,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private void EnsureTrackedScopeWritesAdmitted()
    {
        string[] scopeValues = this.ChangedGrowthScopeValues();
        if (scopeValues.Length == 0)
        {
            return;
        }

        string[] candidateValues = scopeValues
            .SelectMany(ClosedStateCandidateValues)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] candidateHashes = candidateValues
            .Select(AccessScopeIndex.Create)
            .ToArray();
        List<AccessControlScopeState> states = [];
        foreach (string[] batch in candidateHashes.Chunk(
                     ScopeStateQueryBatchSize))
        {
            states.AddRange(this.AccessScopeStates
                .AsNoTracking()
                .Where(state => batch.Contains(state.ScopeHash)));
        }

        EnsureNoHashCollision(states, candidateValues);
        if (states.Any(state =>
                state.IsClosed && candidateValues.Contains(
                    state.ScopeValue,
                    StringComparer.Ordinal)))
        {
            throw new AccessControlScopeClosedException();
        }
    }

    private async Task EnsureTrackedScopeWritesAdmittedAsync(
        CancellationToken cancellationToken)
    {
        string[] scopeValues = this.ChangedGrowthScopeValues();
        if (scopeValues.Length == 0)
        {
            return;
        }

        string[] candidateValues = scopeValues
            .SelectMany(ClosedStateCandidateValues)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] candidateHashes = candidateValues
            .Select(AccessScopeIndex.Create)
            .ToArray();
        List<AccessControlScopeState> states = [];
        foreach (string[] batch in candidateHashes.Chunk(
                     ScopeStateQueryBatchSize))
        {
            states.AddRange(await this.AccessScopeStates
                .AsNoTracking()
                .Where(state => batch.Contains(state.ScopeHash))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false));
        }

        EnsureNoHashCollision(states, candidateValues);
        if (states.Any(state =>
                state.IsClosed && candidateValues.Contains(
                    state.ScopeValue,
                    StringComparer.Ordinal)))
        {
            throw new AccessControlScopeClosedException();
        }
    }

    private string[] ChangedGrowthScopeValues()
    {
        this.ChangeTracker.DetectChanges();
        IEnumerable<string> roleAssignments = this.ChangeTracker
            .Entries<AccessSubjectRoleAssignment>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.ScopeValue);
        IEnumerable<string> profiles = this.ChangeTracker
            .Entries<AccessProfile>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity.OwnerScopeValue);
        IEnumerable<string> profileAssignments = this.ChangeTracker
            .Entries<AccessProfileAssignment>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.AssignmentScopeValue);
        IEnumerable<string> profileChanges = this.ChangeTracker
            .Entries<AccessProfileChange>()
            .Where(entry =>
                entry.State == EntityState.Added &&
                entry.Entity.Kind == AccessProfileChangeKind.Assigned &&
                entry.Entity.AssignmentScopeValue is not null)
            .Select(entry => entry.Entity.AssignmentScopeValue!);
        return roleAssignments
            .Concat(profiles)
            .Concat(profileAssignments)
            .Concat(profileChanges)
            .Where(scopeValue => !string.Equals(
                scopeValue,
                AccessScope.Global.Value,
                StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool IsSameOrDescendant(
        string candidateScopeValue,
        string rootScopeValue) =>
        string.Equals(
            candidateScopeValue,
            rootScopeValue,
            StringComparison.Ordinal) ||
        candidateScopeValue.StartsWith(
            rootScopeValue + "/",
            StringComparison.Ordinal);

    private static string[] ClosedStateCandidateValues(string scopeValue)
    {
        if (!AccessScope.TryParse(scopeValue, out AccessScope? scope) ||
            scope.IsGlobal)
        {
            return [];
        }

        string[] values = new string[scope.Segments.Count];
        for (int index = 0; index < scope.Segments.Count; index++)
        {
            values[index] = string.Join(
                "/",
                scope.Segments.Take(index + 1));
        }

        return values;
    }

    private static void EnsureNoHashCollision(
        IEnumerable<AccessControlScopeState> states,
        IReadOnlyCollection<string> candidateValues)
    {
        foreach (AccessControlScopeState state in states)
        {
            if (!candidateValues.Contains(
                    state.ScopeValue,
                    StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "An access-control lifecycle scope hash collision was detected.");
            }
        }
    }
}
