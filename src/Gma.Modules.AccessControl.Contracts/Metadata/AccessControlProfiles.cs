namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;
using Gma.Framework.ModuleComposition;

public static class AccessControlProfiles
{
    public const string DefaultName = "default";

    public static ModuleProfileDescriptor Default { get; } = new(
        AccessControlModuleMetadata.Name,
        DefaultName,
        provides:
        [
            new ProvidedCompositionFeature(
                new CompositionFeatureId(AccessControlCompositionFeatures.Authorization),
                Provider(DefaultName),
                "Persisted access-control authorization and grant-scope lookup are registered.")
        ],
        displayName: "AccessControl default",
        description: "Optional persisted RBAC provider for generic permission/scope authorization.");

    private static string Provider(string profileName) => $"{AccessControlModuleMetadata.Name}/{profileName}";
}
