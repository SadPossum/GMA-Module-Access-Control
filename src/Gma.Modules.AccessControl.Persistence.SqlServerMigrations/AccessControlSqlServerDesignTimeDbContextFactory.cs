namespace Gma.Modules.AccessControl.Persistence.SqlServerMigrations;

using Gma.Framework.Persistence.EntityFrameworkCore;
using Gma.Modules.AccessControl.Persistence;
using Microsoft.EntityFrameworkCore.Design;

public sealed class AccessControlSqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AccessControlDbContext>
{
    public AccessControlDbContext CreateDbContext(string[] args)
    {
        return new AccessControlDbContext(
            DesignTimeDbContextOptionsFactory.CreateSqlServerOptions<AccessControlDbContext>(
                args,
                AccessControlMigrations.SqlServerAssembly,
                AccessControlMigrations.Schema,
                AccessControlMigrations.HistoryTable));
    }
}
