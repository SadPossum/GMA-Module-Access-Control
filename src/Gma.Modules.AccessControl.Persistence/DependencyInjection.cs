namespace Gma.Modules.AccessControl.Persistence;

using Gma.Framework.Cqrs.UnitOfWork;
using Gma.Framework.Persistence.EntityFrameworkCore;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddAccessControlPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddPersistenceOptions(builder.Configuration);

        builder.Services.TryAddModuleDbContext<AccessControlDbContext>(options =>
            options.UseConfiguredProvider(
                builder.Configuration,
                AccessControlMigrations.SqlServerAssembly,
                AccessControlMigrations.PostgreSqlAssembly,
                AccessControlMigrations.Schema,
                AccessControlMigrations.HistoryTable));

        builder.Services.TryAddScoped<IAccessControlRbacRepository, AccessControlRbacRepository>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IUnitOfWork, AccessControlUnitOfWork>());

        return builder;
    }
}
