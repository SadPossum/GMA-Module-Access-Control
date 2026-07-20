namespace Gma.Modules.AccessControl.Persistence;

using System.Data;
using Gma.Framework.Cqrs.UnitOfWork;
using Gma.Modules.AccessControl.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

internal sealed class AccessControlUnitOfWork(AccessControlDbContext dbContext) : ITransactionalUnitOfWork
{
    private IDbContextTransaction? ownedTransaction;

    public string ModuleName => AccessControlModuleMetadata.Name;

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.ChangeTracker.HasChanges()
            ? dbContext.SaveChangesAsync(cancellationToken)
            : Task.CompletedTask;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (this.ownedTransaction is not null)
        {
            throw new InvalidOperationException("The unit of work already owns an active transaction.");
        }

        if (!dbContext.Database.IsRelational())
        {
            return;
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            this.ownedTransaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            await AccessControlManagementLock.AcquireAsync(dbContext, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await this.RollbackTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (this.ownedTransaction is null)
        {
            return;
        }

        IDbContextTransaction transaction = this.ownedTransaction;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        this.ownedTransaction = null;
        await transaction.DisposeAsync().ConfigureAwait(false);
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (this.ownedTransaction is null)
        {
            return;
        }

        IDbContextTransaction transaction = this.ownedTransaction;
        try
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this.ownedTransaction = null;
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }
}
