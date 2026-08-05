namespace Gma.Modules.AccessControl.Persistence;

using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

internal static class AccessControlManagementLock
{
    public static async Task AcquireAsync(
        AccessControlDbContext dbContext,
        CancellationToken cancellationToken)
    {
        int acquired = await dbContext.BootstrapState
            .Where(state =>
                state.Id == AccessBootstrapState.SingletonId &&
                state.ManagementRevision < long.MaxValue)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    state => state.ManagementRevision,
                    state => state.ManagementRevision + 1),
                cancellationToken)
            .ConfigureAwait(false);
        if (acquired != 1)
        {
            throw new InvalidOperationException("The access-control management safety lock is unavailable.");
        }
    }

    public static async Task<long> AcquireAndReadRevisionAsync(
        AccessControlDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await AcquireAsync(dbContext, cancellationToken).ConfigureAwait(false);
        return await ReadRevisionAsync(dbContext, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<long> ReadRevisionAsync(
        AccessControlDbContext dbContext,
        CancellationToken cancellationToken) =>
        await dbContext.BootstrapState
            .AsNoTracking()
            .Where(state => state.Id == AccessBootstrapState.SingletonId)
            .Select(state => state.ManagementRevision)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
}
