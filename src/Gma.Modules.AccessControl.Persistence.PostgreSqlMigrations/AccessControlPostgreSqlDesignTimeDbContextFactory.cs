namespace Gma.Modules.AccessControl.Persistence.PostgreSqlMigrations;

using Gma.Framework.Persistence.EntityFrameworkCore;
using Gma.Modules.AccessControl.Persistence;
using Microsoft.EntityFrameworkCore.Design;

public sealed class AccessControlPostgreSqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AccessControlDbContext>
{
    public AccessControlDbContext CreateDbContext(string[] args)
    {
        return new AccessControlDbContext(
            DesignTimeDbContextOptionsFactory.CreatePostgreSqlOptions<AccessControlDbContext>(
                args,
                AccessControlMigrations.PostgreSqlAssembly,
                AccessControlMigrations.Schema,
                AccessControlMigrations.HistoryTable));
    }
}
