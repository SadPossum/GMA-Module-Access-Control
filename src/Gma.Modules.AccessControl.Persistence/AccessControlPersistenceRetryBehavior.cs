namespace Gma.Modules.AccessControl.Persistence;

using Gma.Framework.Cqrs;
using Gma.Framework.Observability.Infrastructure;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal sealed class AccessControlPersistenceRetryBehavior<TCommand, TResponse>(
    AccessControlDbContext dbContext) : ICommandPipelineBehavior<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(
        TCommand command,
        CommandNext<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(next);

        if (!string.Equals(
                ModuleNameResolver.FromType(typeof(TCommand)),
                AccessControlModuleMetadata.Name,
                StringComparison.Ordinal))
        {
            return await next().ConfigureAwait(false);
        }

        try
        {
            return await next().ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return await next().ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (AccessControlUniqueConstraintDetector.IsUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            return await next().ConfigureAwait(false);
        }
    }
}

internal static class AccessControlUniqueConstraintDetector
{
    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres &&
                string.Equals(postgres.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal))
            {
                return true;
            }

            if (current is SqlException sqlServer && sqlServer.Number is 2601 or 2627)
            {
                return true;
            }
        }

        return false;
    }
}
