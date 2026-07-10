namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.ModuleComposition;
using Gma.Modules.AccessControl.Contracts;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessControlProfileTests
{
    [Fact]
    public void Default_profile_documents_persisted_authorization_capability()
    {
        ModuleProfileDescriptor profile = AccessControlProfiles.Default;

        Assert.Equal(AccessControlModuleMetadata.Name, profile.ModuleName);
        Assert.Equal(AccessControlProfiles.DefaultName, profile.ProfileName);
        Assert.Contains(profile.Provides, feature =>
            feature.Id == new CompositionFeatureId(AccessControlCompositionFeatures.Authorization));
        Assert.Empty(profile.Requires);
    }

    [Fact]
    public void Descriptor_exposes_default_profile()
    {
        ModuleProfileDescriptor profile = Assert.Single(AccessControlModuleMetadata.Descriptor.GetCompositionProfiles());

        Assert.Equal(AccessControlProfiles.DefaultName, profile.ProfileName);
    }
}
