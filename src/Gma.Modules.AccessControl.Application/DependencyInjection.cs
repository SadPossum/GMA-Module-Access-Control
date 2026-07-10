namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Framework.Application.Composition;
using Gma.Modules.AccessControl.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

public static class DependencyInjection
{
    public static IServiceCollection AddAccessControlApplication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(AccessControlOptionsRegistrationMarker)))
        {
            AccessControlOptionsValidation.GetValidatedOptions(configuration);
            services.AddSingleton<AccessControlOptionsRegistrationMarker>();
            services
                .AddOptions<AccessControlOptions>()
                .Bind(configuration.GetSection(AccessControlOptions.SectionName))
                .ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<AccessControlOptions>, AccessControlOptionsValidator>());
        }

        services.AddGmaAccessControlPermissionPolicies(AccessControlModuleMetadata.Descriptor);
        services.TryAddScoped<PersistedAccessControlDecisionProvider>();
        services.TryAddScoped<IAccessGrantScopeReader, PersistedAccessGrantScopeReader>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccessDecisionProvider, PersistedAccessControlDecisionProvider>());
        services.AddApplicationServicesFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }

    private sealed class AccessControlOptionsRegistrationMarker;
}
