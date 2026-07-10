namespace Gma.Modules.AccessControl.Persistence;

using Gma.Modules.AccessControl.Contracts;

public static class AccessControlMigrations
{
    public const string Schema = AccessControlModuleMetadata.Schema;
    public const string HistoryTable = "__ef_migrations_history";
    public const string SqlServerAssembly = "Gma.Modules.AccessControl.Persistence.SqlServerMigrations";
    public const string PostgreSqlAssembly = "Gma.Modules.AccessControl.Persistence.PostgreSqlMigrations";
}
