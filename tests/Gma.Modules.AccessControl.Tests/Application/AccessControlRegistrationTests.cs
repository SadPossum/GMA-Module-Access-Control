namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Cqrs.UnitOfWork;
using Gma.Framework.Persistence.EntityFrameworkCore;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Handlers;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Application.Queries;
using Gma.Modules.AccessControl.Application.Validation;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessControlRegistrationTests
{
    [Fact]
    public void Application_registration_is_idempotent()
    {
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder().Build();

        services.AddAccessControlApplication(configuration);
        services.AddAccessControlApplication(configuration);

        Assert.Single(services, HasService<IValidateOptions<AccessControlOptions>, AccessControlOptionsValidator>());
        Assert.Single(services, HasService<ICommandHandler<BootstrapOwnerCommand, Unit>, BootstrapOwnerCommandHandler>());
        Assert.Single(services, HasService<ICommandValidator<BootstrapOwnerCommand>, BootstrapOwnerCommandValidator>());
        Assert.Single(services, HasService<ICommandValidator<CreateRoleCommand>, CreateRoleCommandValidator>());
        Assert.Single(services, HasService<ICommandValidator<GrantRolePermissionCommand>, GrantRolePermissionCommandValidator>());
        Assert.Single(services, HasService<ICommandValidator<AssignRoleCommand>, AssignRoleCommandValidator>());
        Assert.Single(services, HasService<ICommandValidator<UnassignRoleCommand>, UnassignRoleCommandValidator>());
        Assert.Single(services, HasService<ICommandValidator<RevokeRolePermissionCommand>, RevokeRolePermissionCommandValidator>());
        Assert.Single(services, HasService<ICommandHandler<UnassignRoleCommand, Unit>, UnassignRoleCommandHandler>());
        Assert.Single(services, HasService<ICommandHandler<RevokeRolePermissionCommand, Unit>, RevokeRolePermissionCommandHandler>());
        Assert.Single(services, HasService<IQueryHandler<ListRoleAssignmentsQuery, IReadOnlyList<AccessControlRoleAssignmentDetails>>, ListRoleAssignmentsQueryHandler>());
        Assert.Single(services, HasService<IAccessDecisionProvider, PersistedAccessControlDecisionProvider>());
        Assert.Single(services, HasService<IAccessGrantScopeReader, PersistedAccessGrantScopeReader>());
        Assert.Single(services, descriptor => descriptor.ServiceType.Name == "AccessControlOptionsRegistrationMarker");
    }

    [Fact]
    public void Application_registration_rejects_invalid_options_before_service_mutation()
    {
        ServiceCollection services = new();
        IConfiguration configuration = CreateConfiguration(
            ("AccessControl:Bootstrap:OwnerRoleName", "Owner Role"));

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
            services.AddAccessControlApplication(configuration));

        Assert.Contains(exception.Failures, failure => failure.Contains("OwnerRoleName", StringComparison.Ordinal));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IValidateOptions<AccessControlOptions>));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ICommandHandler<BootstrapOwnerCommand, Unit>));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType.Name == "AccessControlOptionsRegistrationMarker");
    }

    [Fact]
    public void Application_registration_rejects_null_arguments()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(() =>
            Gma.Modules.AccessControl.Application.DependencyInjection.AddAccessControlApplication(null!, configuration));
        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddAccessControlApplication(null!));
    }

    [Fact]
    public void Persistence_registration_is_explicit_and_idempotent()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>("Persistence:Provider", "SqlServer"),
            new KeyValuePair<string, string?>("ConnectionStrings:SqlServer", "Server=localhost;Database=gma;TrustServerCertificate=True")
        ]);

        builder.AddAccessControlPersistence();
        builder.AddAccessControlPersistence();

        Assert.Single(builder.Services, descriptor => descriptor.ServiceType == typeof(AccessControlDbContext));
        Assert.Single(builder.Services, HasService<IAccessControlRbacRepository, AccessControlRbacRepository>());
        Assert.Single(builder.Services, HasService<IUnitOfWork, AccessControlUnitOfWork>());
    }

    [Fact]
    public void Migrations_are_provider_split()
    {
        Type sqlServerFactory =
            typeof(Gma.Modules.AccessControl.Persistence.SqlServerMigrations.AccessControlSqlServerDesignTimeDbContextFactory);
        Type postgreSqlFactory =
            typeof(Gma.Modules.AccessControl.Persistence.PostgreSqlMigrations.AccessControlPostgreSqlDesignTimeDbContextFactory);

        Assert.Contains(sqlServerFactory.Assembly.GetTypes(), type => type.Name == "InitialAccessControlSchema");
        Assert.Contains(postgreSqlFactory.Assembly.GetTypes(), type => type.Name == "InitialAccessControlSchema");
        Assert.Equal("access", AccessControlMigrations.Schema);
        Assert.Equal("__ef_migrations_history", AccessControlMigrations.HistoryTable);
    }

    private static Predicate<ServiceDescriptor> HasService<TService, TImplementation>() =>
        descriptor =>
            descriptor.ServiceType == typeof(TService) &&
            descriptor.ImplementationType == typeof(TImplementation);

    private static IConfiguration CreateConfiguration(params (string Key, string Value)[] values)
    {
        ConfigurationBuilder builder = new();
        builder.AddInMemoryCollection(values.Select(item =>
            new KeyValuePair<string, string?>(item.Key, item.Value)));

        return builder.Build();
    }
}
