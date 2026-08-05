namespace Gma.Modules.AccessControl.Persistence;

using System.Data;
using Gma.Framework.Messaging;
using Gma.Framework.Messaging.Infrastructure;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

internal sealed class AccessControlInboxStore(
    AccessControlDbContext dbContext,
    ISystemClock clock,
    IIdGenerator idGenerator)
    : EfInboxStore<AccessControlDbContext>(
        dbContext,
        clock,
        idGenerator,
        AccessControlModuleMetadata.Name)
{
    protected override async ValueTask<bool> IsAdmittedAsync(
        InboxMessageRecord message,
        CancellationToken cancellationToken)
    {
        if (message.ScopeId is null)
        {
            return true;
        }

        await AccessControlManagementLock.AcquireAsync(
                this.DbContext,
                cancellationToken)
            .ConfigureAwait(false);
        return !await this.DbContext.IsTransportScopeClosedAsync(
                message.ScopeId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<int> DeleteProcessedBeforeAsync(
        DateTimeOffset processedBeforeUtc,
        int maxMessages,
        CancellationToken cancellationToken)
    {
        ValidateCleanupArguments(processedBeforeUtc, maxMessages);
        await using IDbContextTransaction? transaction =
            await this.BeginSerializableTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        IQueryable<InboxMessage> candidates = this.CleanupCandidates(
            processedBeforeUtc,
            maxMessages);
        if (!await candidates.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            await CommitAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            return 0;
        }

        await AccessControlManagementLock.AcquireAsync(
                this.DbContext,
                cancellationToken)
            .ConfigureAwait(false);
        candidates = this.CleanupCandidates(processedBeforeUtc, maxMessages);
        InboxCleanupCandidate[] selected = await candidates
            .Select(message => new InboxCleanupCandidate(
                message.Id,
                message.Handler))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (selected.Length == 0)
        {
            await CommitAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            return 0;
        }

        int removed = await candidates
            .Take(selected.Length)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (removed != selected.Length)
        {
            throw new InvalidDataException(
                "Access-control inbox cleanup changed during its revision fence.");
        }

        await CommitAsync(transaction, cancellationToken)
            .ConfigureAwait(false);
        return removed;
    }

    private IQueryable<InboxMessage> CleanupCandidates(
        DateTimeOffset processedBeforeUtc,
        int maxMessages) =>
        this.DbContext.InboxMessages
            .Where(message =>
                message.Status == InboxMessageStatus.Processed &&
                message.ProcessedAtUtc != null &&
                message.ProcessedAtUtc < processedBeforeUtc)
            .Where(message =>
                message.ScopeId == null ||
                !this.DbContext.AccessScopeStates.Any(state =>
                    state.TransportScopeId == message.ScopeId &&
                    state.IsClosed))
            .OrderBy(message => message.ProcessedAtUtc)
            .ThenBy(message => message.Id)
            .ThenBy(message => message.Handler)
            .Take(maxMessages);

    private async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (!this.DbContext.Database.IsRelational() ||
            this.DbContext.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return await this.DbContext.Database.BeginTransactionAsync(
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

    private static void ValidateCleanupArguments(
        DateTimeOffset processedBeforeUtc,
        int maxMessages)
    {
        if (processedBeforeUtc == default)
        {
            throw new ArgumentException(
                $"{nameof(processedBeforeUtc)} must not be the default timestamp.",
                nameof(processedBeforeUtc));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxMessages, 1);
    }

    private sealed record InboxCleanupCandidate(Guid Id, string Handler);
}
