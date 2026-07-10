namespace Gma.Modules.AccessControl.Persistence;

using Gma.Framework.Cqrs.UnitOfWork;
using Gma.Modules.AccessControl.Contracts;

internal sealed class AccessControlUnitOfWork(AccessControlDbContext dbContext) : IUnitOfWork
{
    public string ModuleName => AccessControlModuleMetadata.Name;

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.ChangeTracker.HasChanges()
            ? dbContext.SaveChangesAsync(cancellationToken)
            : Task.CompletedTask;
}
