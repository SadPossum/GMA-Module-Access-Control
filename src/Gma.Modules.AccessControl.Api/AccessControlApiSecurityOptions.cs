namespace Gma.Modules.AccessControl.Api;

using Gma.Framework.Security;

public sealed class AccessControlApiSecurityOptions
{
    public AuthenticationAssuranceRequirement? ProfileManagementAssurance { get; set; }
}
