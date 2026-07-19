namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessControlPersistenceRetryBehaviorTests
{
    [Fact]
    public async Task Concurrency_failure_clears_tracking_and_retries_once()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        dbContext.Roles.Add(new AccessRole(Guid.NewGuid(), "reader", DateTimeOffset.UtcNow));
        AccessControlPersistenceRetryBehavior<CreateRoleCommand, AccessControlRoleDetails> behavior = new(dbContext);
        int attempts = 0;

        Result<AccessControlRoleDetails> result = await behavior.HandleAsync(
            new CreateRoleCommand("reader"),
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    Assert.NotEmpty(dbContext.ChangeTracker.Entries());
                    throw new DbUpdateConcurrencyException("simulated optimistic concurrency race");
                }

                Assert.Empty(dbContext.ChangeTracker.Entries());
                return Task.FromResult(Result.Success(new AccessControlRoleDetails(
                    Guid.NewGuid(),
                    "reader",
                    [],
                    0)));
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task PostgreSql_unique_failure_clears_tracking_and_retries_once()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        dbContext.Roles.Add(new AccessRole(Guid.NewGuid(), "reader", DateTimeOffset.UtcNow));
        AccessControlPersistenceRetryBehavior<CreateRoleCommand, AccessControlRoleDetails> behavior = new(dbContext);
        int attempts = 0;

        Result<AccessControlRoleDetails> result = await behavior.HandleAsync(
            new CreateRoleCommand("reader"),
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new DbUpdateException(
                        "simulated unique constraint race",
                        new PostgresException("duplicate", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));
                }

                Assert.Empty(dbContext.ChangeTracker.Entries());
                return Task.FromResult(Result.Failure<AccessControlRoleDetails>(
                    AccessControlApplicationErrors.RoleAlreadyExists));
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccessControlApplicationErrors.RoleAlreadyExists, result.Error);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Non_unique_database_failure_is_not_classified_as_retryable()
    {
        DbUpdateException exception = new(
            "simulated database failure",
            new PostgresException("failure", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation));

        Assert.False(AccessControlUniqueConstraintDetector.IsUniqueViolation(exception));
    }

    private static AccessControlDbContext CreateDbContext()
    {
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AccessControlDbContext(options);
    }
}
