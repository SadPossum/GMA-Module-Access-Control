namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Framework.Application.Composition;
using Gma.Framework.Cqrs;
using Gma.Framework.Permissions;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

public static class DependencyInjection
{
    public static IServiceCollection AddAccessProfilePermissionAllowlist(
        this IServiceCollection services,
        IEnumerable<string> permissionCodes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(permissionCodes);

        foreach (string permissionCode in permissionCodes)
        {
            PermissionCode permission = PermissionCode.Create(permissionCode);
            bool exists = services
                .Where(descriptor => descriptor.ServiceType == typeof(AccessProfileAllowedPermission))
                .Select(descriptor => descriptor.ImplementationInstance)
                .OfType<AccessProfileAllowedPermission>()
                .Any(registration => registration.Permission == permission);
            if (!exists)
            {
                services.AddSingleton(new AccessProfileAllowedPermission(permission));
            }
        }

        return services;
    }

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
        services.TryAddScoped<IAccessControlRoleProvisioner, AccessControlRoleProvisioner>();
        services.TryAddScoped<AccessRoleAssignmentPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            ICommandOutcomeObserver<AssignRoleCommand, Unit>,
            RoleAssignmentLifecycleOutcomeObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            ICommandOutcomeObserver<UnassignRoleCommand, Unit>,
            RoleAssignmentLifecycleOutcomeObserver>());
        services.TryAddScoped<AccessProfilePermissionPolicy>();
        services.TryAddScoped<AccessProfileAssignmentPolicy>();
        services.TryAddScoped<AccessProfileMutationAdmissionPolicy>();
        services.TryAddScoped<AccessControlScopeWriteAdmission>();
        services.TryAddScoped<IAccessProfileManager, AccessProfileManager>();
        services.TryAddScoped<IScopedAccessProfileManager, ScopedAccessProfileManager>();
        services.TryAddScoped<IAccessProfileProvisioner, AccessProfileProvisioner>();
        services.TryAddScoped<IScopedAccessProfileProvisioner, AccessProfileProvisioner>();
        services.TryAddScoped<IAccessProfileAssignmentRevoker, AccessProfileAssignmentRevoker>();
        services.TryAddScoped<IAccessGrantScopeReader, PersistedAccessGrantScopeReader>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccessDecisionProvider, PersistedAccessControlDecisionProvider>());
        services.AddApplicationServicesFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }

    private sealed class AccessControlOptionsRegistrationMarker;
}
