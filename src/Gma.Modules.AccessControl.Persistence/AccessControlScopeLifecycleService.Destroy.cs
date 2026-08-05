namespace Gma.Modules.AccessControl.Persistence;

using System.Data;
using System.Globalization;
using Gma.Framework.AccessControl;
using Gma.Framework.Messaging.Infrastructure;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ContractDestroyReceipt =
    Gma.Modules.AccessControl.Contracts.AccessControlScopeDestroyReceipt;
using DomainDestroyOperation =
    Gma.Modules.AccessControl.Persistence.Entities.AccessControlScopeDestroyOperation;
using DomainDestroyReceipt =
    Gma.Modules.AccessControl.Persistence.Entities.AccessControlScopeDestroyReceipt;
using DomainDestroyStage =
    Gma.Modules.AccessControl.Persistence.Entities.AccessControlScopeDestroyStage;

internal sealed partial class AccessControlScopeLifecycleService
{
    public async Task<AccessControlScopeDestroyResult> DestroyBatchAsync(
        AccessControlScopeDestroyRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            request.OperationId == Guid.Empty ||
            !TryNormalize(request.Coordinate, out NormalizedCoordinate? coordinate) ||
            request.ExpectedRevision < 0 ||
            request.ExpectedRevision == long.MaxValue ||
            request.BatchSize is < 1 or >
                AccessControlScopeLifecycleLimits.MaximumDestroyBatchSize)
        {
            return DestroyResult(AccessControlScopeDestroyStatus.Invalid);
        }

        string requestSha256 = DestroyRequestSha256(request, coordinate);
        AccessControlScopeDestroyResult? completed = await this
            .TryReadCompletedResultAsync(
                request.OperationId,
                coordinate,
                requestSha256,
                cancellationToken)
            .ConfigureAwait(false);
        if (completed is not null)
        {
            return completed;
        }

        await using IDbContextTransaction? transaction =
            await this.BeginSerializableTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        try
        {
            long acquiredRevision = await AccessControlManagementLock
                .AcquireAndReadRevisionAsync(dbContext, cancellationToken)
                .ConfigureAwait(false);

            DomainDestroyReceipt[] receiptMatches = await dbContext
                .AccessScopeDestroyReceipts
                .Where(receipt =>
                    receipt.OperationId == request.OperationId ||
                    receipt.ScopeHash == coordinate.ScopeHash ||
                    receipt.TransportScopeId == coordinate.TransportScopeId)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (receiptMatches.Length > 0)
            {
                DomainDestroyReceipt? receipt = receiptMatches
                    .SingleOrDefault(candidate =>
                        Matches(candidate, coordinate));
                AccessControlScopeDestroyResult result =
                    receipt is not null && receiptMatches.Length == 1 &&
                    receipt.Matches(request.OperationId, requestSha256)
                        ? DestroyResult(
                            AccessControlScopeDestroyStatus.Replayed,
                            receipt: Map(receipt))
                        : DestroyResult(
                            AccessControlScopeDestroyStatus.Conflict);
                await CommitAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);
                return result;
            }

            DomainDestroyOperation[] operationMatches = await dbContext
                .AccessScopeDestroyOperations
                .Where(operation =>
                    operation.OperationId == request.OperationId ||
                    operation.ScopeHash == coordinate.ScopeHash ||
                    operation.TransportScopeId == coordinate.TransportScopeId)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            DomainDestroyOperation? operation = operationMatches
                .SingleOrDefault(candidate => Matches(candidate, coordinate));
            if (operationMatches.Length > 0 &&
                (operation is null || operationMatches.Length != 1 ||
                 !operation.Matches(request.OperationId, requestSha256)))
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return DestroyResult(AccessControlScopeDestroyStatus.Conflict);
            }

            AccessControlScopeState[] stateMatches = await dbContext
                .AccessScopeStates
                .Where(state =>
                    state.ScopeHash == coordinate.ScopeHash ||
                    state.TransportScopeId == coordinate.TransportScopeId)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            AccessControlScopeState? state = stateMatches
                .SingleOrDefault(candidate => Matches(candidate, coordinate));
            if (stateMatches.Length > 0 &&
                (state is null || stateMatches.Length != 1))
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return DestroyResult(AccessControlScopeDestroyStatus.Conflict);
            }

            bool activeWorkChecked = false;
            if (operation is null)
            {
                if (state is not null)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return DestroyResult(
                        AccessControlScopeDestroyStatus.Conflict);
                }

                bool hasScopeData = await this.HasScopeDataAsync(
                        coordinate,
                        cancellationToken)
                    .ConfigureAwait(false);
                long selectedRevision = acquiredRevision - 1;
                if ((hasScopeData &&
                     request.ExpectedRevision != selectedRevision) ||
                    (!hasScopeData && request.ExpectedRevision != 0))
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return DestroyResult(
                        AccessControlScopeDestroyStatus.Stale);
                }

                if (await this.HasActiveScopeWorkAsync(
                        coordinate.TransportScopeId,
                        cancellationToken).ConfigureAwait(false))
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return DestroyResult(AccessControlScopeDestroyStatus.Busy);
                }

                activeWorkChecked = true;
                state = AccessControlScopeState.Create(
                    coordinate.ScopeValue,
                    coordinate.TransportScopeId);
                DateTimeOffset startedAtUtc = clock.UtcNow;
                if (state.Close(
                        request.OperationId,
                        requestSha256,
                        acquiredRevision,
                        startedAtUtc) !=
                    AccessControlScopeCloseTransition.Completed)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return DestroyResult(
                        AccessControlScopeDestroyStatus.Conflict);
                }

                operation = DomainDestroyOperation.TryCreate(
                    request.OperationId,
                    coordinate.ScopeValue,
                    coordinate.TransportScopeId,
                    requestSha256,
                    request.ExpectedRevision,
                    acquiredRevision,
                    request.BatchSize,
                    AccessControlScopeLifecycleLimits.MaximumDestroyBatchSize,
                    startedAtUtc);
                if (operation is null)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return DestroyResult(
                        AccessControlScopeDestroyStatus.Invalid);
                }

                await dbContext.AccessScopeStates.AddAsync(
                        state,
                        cancellationToken)
                    .ConfigureAwait(false);
                await dbContext.AccessScopeDestroyOperations.AddAsync(
                        operation,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (!Matches(operation, state, requestSha256))
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return DestroyResult(AccessControlScopeDestroyStatus.Conflict);
            }

            if (!activeWorkChecked &&
                await this.HasActiveScopeWorkAsync(
                    coordinate.TransportScopeId,
                    cancellationToken).ConfigureAwait(false))
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return DestroyResult(
                    AccessControlScopeDestroyStatus.Busy,
                    Map(operation));
            }

            while (!operation.IsComplete)
            {
                DestroyRecordKey[] loadedKeys = await this.LoadStageKeysAsync(
                        coordinate,
                        operation,
                        operation.BatchSize + 1,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (loadedKeys.Length == 0)
                {
                    if (!operation.AdvanceEmptyStage(clock.UtcNow))
                    {
                        throw new InvalidDataException(
                            "Access-control scope destruction stage progress is invalid.");
                    }

                    continue;
                }

                DestroyRecordKey[] selectedKeys = loadedKeys
                    .Take(operation.BatchSize)
                    .ToArray();
                int removed = await this.DeleteStageKeysAsync(
                        coordinate,
                        operation,
                        selectedKeys,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (removed != selectedKeys.Length ||
                    !operation.RecordBatch(
                        operation.Stage,
                        removed,
                        KeysSha256(selectedKeys),
                        stageCompleted:
                            loadedKeys.Length <= operation.BatchSize,
                        clock.UtcNow))
                {
                    throw new InvalidDataException(
                        "Access-control scope destruction batch progress is invalid.");
                }

                break;
            }

            if (!operation.IsComplete)
            {
                await dbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);
                await CommitAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);
                return DestroyResult(
                    AccessControlScopeDestroyStatus.InProgress,
                    Map(operation));
            }

            DomainDestroyReceipt? completedReceipt =
                DomainDestroyReceipt.TryCreate(operation, clock.UtcNow);
            if (completedReceipt is null)
            {
                throw new InvalidDataException(
                    "Access-control scope destruction receipt is invalid.");
            }

            await dbContext.AccessScopeDestroyReceipts.AddAsync(
                    completedReceipt,
                    cancellationToken)
                .ConfigureAwait(false);
            dbContext.AccessScopeDestroyOperations.Remove(operation);
            await dbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await CommitAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            return DestroyResult(
                AccessControlScopeDestroyStatus.Completed,
                receipt: Map(completedReceipt));
        }
        catch
        {
            await RollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<AccessControlScopeDestroyResult?>
        TryReadCompletedResultAsync(
            Guid operationId,
            NormalizedCoordinate coordinate,
            string requestSha256,
            CancellationToken cancellationToken)
    {
        DomainDestroyReceipt[] receipts = await dbContext
            .AccessScopeDestroyReceipts
            .AsNoTracking()
            .Where(receipt =>
                receipt.OperationId == operationId ||
                receipt.ScopeHash == coordinate.ScopeHash ||
                receipt.TransportScopeId == coordinate.TransportScopeId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (receipts.Length == 0)
        {
            return null;
        }

        DomainDestroyReceipt? exact = receipts.SingleOrDefault(receipt =>
            Matches(receipt, coordinate));
        return exact is not null && receipts.Length == 1 &&
            exact.Matches(operationId, requestSha256)
                ? DestroyResult(
                    AccessControlScopeDestroyStatus.Replayed,
                    receipt: Map(exact))
                : DestroyResult(AccessControlScopeDestroyStatus.Conflict);
    }

    private Task<bool> HasActiveScopeWorkAsync(
        string transportScopeId,
        CancellationToken cancellationToken) =>
        dbContext.InboxMessages.AnyAsync(
            message =>
                message.ScopeId == transportScopeId &&
                message.Status == InboxMessageStatus.Processing,
            cancellationToken);

    private Task<DestroyRecordKey[]> LoadStageKeysAsync(
        NormalizedCoordinate coordinate,
        DomainDestroyOperation operation,
        int take,
        CancellationToken cancellationToken)
    {
        string root = coordinate.ScopeValue;
        string prefix = root + "/";
        return operation.Stage switch
        {
            DomainDestroyStage.InboxMessages => dbContext.InboxMessages
                .Where(message =>
                    message.ScopeId == coordinate.TransportScopeId)
                .OrderBy(message => message.Id)
                .ThenBy(message => message.Handler)
                .Select(message => new DestroyRecordKey(
                    message.Id,
                    message.Handler,
                    null,
                    null))
                .Take(take)
                .ToArrayAsync(cancellationToken),
            DomainDestroyStage.ProfileChanges => dbContext
                .AccessProfileChanges
                .Where(change =>
                    dbContext.AccessProfiles.Any(profile =>
                        profile.Id == change.ProfileId &&
                        (profile.OwnerScopeValue == root ||
                         profile.OwnerScopeValue.StartsWith(prefix))) ||
                    change.AssignmentScopeValue == root ||
                    (change.AssignmentScopeValue != null &&
                     change.AssignmentScopeValue.StartsWith(prefix)))
                .OrderBy(change => change.Id)
                .Select(change => new DestroyRecordKey(
                    change.Id,
                    null,
                    null,
                    null))
                .Take(take)
                .ToArrayAsync(cancellationToken),
            DomainDestroyStage.ProfileAssignments => dbContext
                .AccessProfileAssignments
                .Where(assignment =>
                    dbContext.AccessProfiles.Any(profile =>
                        profile.Id == assignment.ProfileId &&
                        (profile.OwnerScopeValue == root ||
                         profile.OwnerScopeValue.StartsWith(prefix))) ||
                    assignment.AssignmentScopeValue == root ||
                    assignment.AssignmentScopeValue.StartsWith(prefix))
                .OrderBy(assignment => assignment.Id)
                .Select(assignment => new DestroyRecordKey(
                    assignment.Id,
                    null,
                    assignment.SubjectKind,
                    assignment.SubjectId))
                .Take(take)
                .ToArrayAsync(cancellationToken),
            DomainDestroyStage.RoleAssignments => dbContext
                .SubjectRoleAssignments
                .Where(assignment =>
                    assignment.ScopeValue == root ||
                    assignment.ScopeValue.StartsWith(prefix))
                .OrderBy(assignment => assignment.Id)
                .Select(assignment => new DestroyRecordKey(
                    assignment.Id,
                    null,
                    assignment.SubjectKind,
                    assignment.SubjectId))
                .Take(take)
                .ToArrayAsync(cancellationToken),
            DomainDestroyStage.Profiles => dbContext.AccessProfiles
                .Where(profile =>
                    profile.OwnerScopeValue == root ||
                    profile.OwnerScopeValue.StartsWith(prefix))
                .OrderBy(profile => profile.Id)
                .Select(profile => new DestroyRecordKey(
                    profile.Id,
                    null,
                    null,
                    null))
                .Take(take)
                .ToArrayAsync(cancellationToken),
            DomainDestroyStage.OrphanPrincipals => dbContext
                .AccessScopeDestroyPrincipalCandidates
                .Where(candidate =>
                    candidate.OperationId == operation.OperationId)
                .OrderBy(candidate => candidate.SubjectKind)
                .ThenBy(candidate => candidate.SubjectId)
                .Select(candidate => new DestroyRecordKey(
                    Guid.Empty,
                    null,
                    candidate.SubjectKind,
                    candidate.SubjectId))
                .Take(take)
                .ToArrayAsync(cancellationToken),
            _ => throw new InvalidOperationException(
                "The access-control scope destruction stage is invalid.")
        };
    }

    private async Task<int> DeleteStageKeysAsync(
        NormalizedCoordinate coordinate,
        DomainDestroyOperation operation,
        DestroyRecordKey[] keys,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            throw new NotSupportedException(
                "Access-control scope destruction requires a relational provider.");
        }

        if (operation.Stage is DomainDestroyStage.ProfileAssignments or
            DomainDestroyStage.RoleAssignments)
        {
            await this.RegisterPrincipalCandidatesAsync(
                    operation.OperationId,
                    keys,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        string root = coordinate.ScopeValue;
        string prefix = root + "/";
        int count = keys.Length;
        return operation.Stage switch
        {
            DomainDestroyStage.InboxMessages => await dbContext.InboxMessages
                .Where(message =>
                    message.ScopeId == coordinate.TransportScopeId)
                .OrderBy(message => message.Id)
                .ThenBy(message => message.Handler)
                .Take(count)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false),
            DomainDestroyStage.ProfileChanges => await dbContext
                .AccessProfileChanges
                .Where(change =>
                    dbContext.AccessProfiles.Any(profile =>
                        profile.Id == change.ProfileId &&
                        (profile.OwnerScopeValue == root ||
                         profile.OwnerScopeValue.StartsWith(prefix))) ||
                    change.AssignmentScopeValue == root ||
                    (change.AssignmentScopeValue != null &&
                     change.AssignmentScopeValue.StartsWith(prefix)))
                .OrderBy(change => change.Id)
                .Take(count)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false),
            DomainDestroyStage.ProfileAssignments => await dbContext
                .AccessProfileAssignments
                .Where(assignment =>
                    dbContext.AccessProfiles.Any(profile =>
                        profile.Id == assignment.ProfileId &&
                        (profile.OwnerScopeValue == root ||
                         profile.OwnerScopeValue.StartsWith(prefix))) ||
                    assignment.AssignmentScopeValue == root ||
                    assignment.AssignmentScopeValue.StartsWith(prefix))
                .OrderBy(assignment => assignment.Id)
                .Take(count)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false),
            DomainDestroyStage.RoleAssignments => await dbContext
                .SubjectRoleAssignments
                .Where(assignment =>
                    assignment.ScopeValue == root ||
                    assignment.ScopeValue.StartsWith(prefix))
                .OrderBy(assignment => assignment.Id)
                .Take(count)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false),
            DomainDestroyStage.Profiles => await dbContext.AccessProfiles
                .Where(profile =>
                    profile.OwnerScopeValue == root ||
                    profile.OwnerScopeValue.StartsWith(prefix))
                .OrderBy(profile => profile.Id)
                .Take(count)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false),
            DomainDestroyStage.OrphanPrincipals =>
                await this.DeletePrincipalCandidatesAsync(
                    operation.OperationId,
                    keys,
                    cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "The access-control scope destruction stage is invalid.")
        };
    }

    private async Task RegisterPrincipalCandidatesAsync(
        Guid operationId,
        IEnumerable<DestroyRecordKey> keys,
        CancellationToken cancellationToken)
    {
        SubjectKey[] subjects = keys
            .Where(key => key.SubjectKind.HasValue && key.SubjectId is not null)
            .Select(key => new SubjectKey(
                key.SubjectKind!.Value,
                key.SubjectId!))
            .Distinct()
            .ToArray();
        if (subjects.Length == 0)
        {
            return;
        }

        int[] kinds = subjects.Select(subject => subject.Kind).Distinct().ToArray();
        string[] ids = subjects.Select(subject => subject.Id).Distinct().ToArray();
        SubjectKey[] existing = await dbContext
            .AccessScopeDestroyPrincipalCandidates
            .Where(candidate =>
                candidate.OperationId == operationId &&
                kinds.Contains(candidate.SubjectKind) &&
                ids.Contains(candidate.SubjectId))
            .Select(candidate => new SubjectKey(
                candidate.SubjectKind,
                candidate.SubjectId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        HashSet<SubjectKey> existingSet = existing.ToHashSet();
        foreach (SubjectKey subject in subjects.Where(subject =>
                     !existingSet.Contains(subject)))
        {
            dbContext.AccessScopeDestroyPrincipalCandidates.Add(
                new AccessControlScopeDestroyPrincipalCandidate(
                    operationId,
                    (AccessSubjectKind)subject.Kind,
                    subject.Id));
        }
    }

    private async Task<int> DeletePrincipalCandidatesAsync(
        Guid operationId,
        DestroyRecordKey[] keys,
        CancellationToken cancellationToken)
    {
        SubjectKey[] selected = keys
            .Select(key => new SubjectKey(
                key.SubjectKind!.Value,
                key.SubjectId!))
            .ToArray();
        int[] kinds = selected.Select(subject => subject.Kind).Distinct().ToArray();
        string[] ids = selected.Select(subject => subject.Id).Distinct().ToArray();
        AccessControlScopeDestroyPrincipalCandidate[] candidates =
            await dbContext.AccessScopeDestroyPrincipalCandidates
                .Where(candidate =>
                    candidate.OperationId == operationId &&
                    kinds.Contains(candidate.SubjectKind) &&
                    ids.Contains(candidate.SubjectId))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        HashSet<SubjectKey> selectedSet = selected.ToHashSet();
        candidates = candidates
            .Where(candidate => selectedSet.Contains(new SubjectKey(
                candidate.SubjectKind,
                candidate.SubjectId)))
            .ToArray();
        if (candidates.Length != selected.Length)
        {
            throw new InvalidDataException(
                "Access-control principal candidates changed during destruction.");
        }

        SubjectKey[] roleReferences = await dbContext.SubjectRoleAssignments
            .Where(assignment =>
                kinds.Contains(assignment.SubjectKind) &&
                ids.Contains(assignment.SubjectId))
            .Select(assignment => new SubjectKey(
                assignment.SubjectKind,
                assignment.SubjectId))
            .Distinct()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        SubjectKey[] profileReferences = await dbContext.AccessProfileAssignments
            .Where(assignment =>
                kinds.Contains(assignment.SubjectKind) &&
                ids.Contains(assignment.SubjectId))
            .Select(assignment => new SubjectKey(
                assignment.SubjectKind,
                assignment.SubjectId))
            .Distinct()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        HashSet<SubjectKey> referenced = roleReferences
            .Concat(profileReferences)
            .ToHashSet();
        AccessPrincipal[] principals = await dbContext.Principals
            .Where(principal =>
                kinds.Contains(principal.Kind) &&
                ids.Contains(principal.SubjectId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.Principals.RemoveRange(principals.Where(principal =>
            selectedSet.Contains(new SubjectKey(
                principal.Kind,
                principal.SubjectId)) &&
            !referenced.Contains(new SubjectKey(
                principal.Kind,
                principal.SubjectId))));
        dbContext.AccessScopeDestroyPrincipalCandidates.RemoveRange(candidates);
        return selected.Length;
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational() ||
            dbContext.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static bool Matches(
        DomainDestroyOperation operation,
        AccessControlScopeState? state,
        string requestSha256) =>
        state is not null &&
        state.IsClosed &&
        state.CloseOperationId == operation.OperationId &&
        state.CloseRevision == operation.ResultingRevision &&
        string.Equals(
            state.CloseRequestSha256,
            requestSha256,
            StringComparison.Ordinal) &&
        string.Equals(
            state.ScopeHash,
            operation.ScopeHash,
            StringComparison.Ordinal) &&
        string.Equals(
            state.ScopeValue,
            operation.ScopeValue,
            StringComparison.Ordinal) &&
        string.Equals(
            state.TransportScopeId,
            operation.TransportScopeId,
            StringComparison.Ordinal);

    private static bool Matches(
        AccessControlScopeState state,
        NormalizedCoordinate coordinate) =>
        string.Equals(state.ScopeHash, coordinate.ScopeHash, StringComparison.Ordinal) &&
        string.Equals(state.ScopeValue, coordinate.ScopeValue, StringComparison.Ordinal) &&
        string.Equals(
            state.TransportScopeId,
            coordinate.TransportScopeId,
            StringComparison.Ordinal);

    private static bool Matches(
        DomainDestroyOperation operation,
        NormalizedCoordinate coordinate) =>
        string.Equals(operation.ScopeHash, coordinate.ScopeHash, StringComparison.Ordinal) &&
        string.Equals(operation.ScopeValue, coordinate.ScopeValue, StringComparison.Ordinal) &&
        string.Equals(
            operation.TransportScopeId,
            coordinate.TransportScopeId,
            StringComparison.Ordinal);

    private static bool Matches(
        DomainDestroyReceipt receipt,
        NormalizedCoordinate coordinate) =>
        string.Equals(receipt.ScopeHash, coordinate.ScopeHash, StringComparison.Ordinal) &&
        string.Equals(receipt.ScopeValue, coordinate.ScopeValue, StringComparison.Ordinal) &&
        string.Equals(
            receipt.TransportScopeId,
            coordinate.TransportScopeId,
            StringComparison.Ordinal);

    private static string DestroyRequestSha256(
        AccessControlScopeDestroyRequest request,
        NormalizedCoordinate coordinate) =>
        AccessControlLifecycleHashes.Sha256(
            "gma-access-control-scope-destroy/v1|" +
            $"{coordinate.ScopeValue.Length}:" + coordinate.ScopeValue + "|" +
            $"{coordinate.TransportScopeId.Length}:" +
            coordinate.TransportScopeId + "|" +
            request.ExpectedRevision.ToString(CultureInfo.InvariantCulture) + "|" +
            request.BatchSize.ToString(CultureInfo.InvariantCulture));

    private static string KeysSha256(IEnumerable<DestroyRecordKey> keys) =>
        AccessControlLifecycleHashes.Sha256(string.Join(
            '\n',
            keys.Select(key => key.Canonical)
                .Order(StringComparer.Ordinal)));

    private static AccessControlScopeDestroyProgress Map(
        DomainDestroyOperation operation) =>
        new(
            operation.OperationId,
            operation.ResultingRevision,
            operation.BatchSize,
            ToContract(operation.Stage),
            operation.RemovedRecordCount,
            operation.CompletedBatchCount,
            operation.ProofVersion,
            operation.RemovalProofSha256,
            operation.StartedAtUtc,
            operation.UpdatedAtUtc);

    private static ContractDestroyReceipt Map(DomainDestroyReceipt receipt) =>
        new(
            receipt.OperationId,
            receipt.ResultingRevision,
            receipt.BatchSize,
            receipt.RemovedRecordCount,
            receipt.CompletedBatchCount,
            receipt.RemovalProofVersion,
            receipt.RemovalProofSha256,
            receipt.StartedAtUtc,
            receipt.CompletedAtUtc);

    private static AccessControlScopeDestructionStage ToContract(
        DomainDestroyStage stage) =>
        stage switch
        {
            DomainDestroyStage.InboxMessages =>
                AccessControlScopeDestructionStage.InboxMessages,
            DomainDestroyStage.ProfileChanges =>
                AccessControlScopeDestructionStage.ProfileChanges,
            DomainDestroyStage.ProfileAssignments =>
                AccessControlScopeDestructionStage.ProfileAssignments,
            DomainDestroyStage.RoleAssignments =>
                AccessControlScopeDestructionStage.RoleAssignments,
            DomainDestroyStage.Profiles =>
                AccessControlScopeDestructionStage.Profiles,
            DomainDestroyStage.OrphanPrincipals =>
                AccessControlScopeDestructionStage.OrphanPrincipals,
            DomainDestroyStage.Completed =>
                AccessControlScopeDestructionStage.Completed,
            _ => AccessControlScopeDestructionStage.Unknown
        };

    private static AccessControlScopeDestroyResult DestroyResult(
        AccessControlScopeDestroyStatus status,
        AccessControlScopeDestroyProgress? progress = null,
        ContractDestroyReceipt? receipt = null) =>
        new(status, progress, receipt);

    private sealed record DestroyRecordKey(
        Guid Id,
        string? Discriminator,
        int? SubjectKind,
        string? SubjectId)
    {
        public string Canonical => this.SubjectKind.HasValue
            ? "subject|" +
              this.SubjectKind.Value.ToString(CultureInfo.InvariantCulture) +
              "|" + this.SubjectId!.Length.ToString(
                  CultureInfo.InvariantCulture) + ":" + this.SubjectId
            : this.Discriminator is null
                ? this.Id.ToString("D") + "|-"
                : this.Id.ToString("D") + "|" +
                  this.Discriminator.Length.ToString(
                      CultureInfo.InvariantCulture) + ':' + this.Discriminator;
    }

    private sealed record SubjectKey(int Kind, string Id);
}
