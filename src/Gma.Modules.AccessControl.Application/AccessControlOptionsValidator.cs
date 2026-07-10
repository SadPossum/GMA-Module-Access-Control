namespace Gma.Modules.AccessControl.Application;

using Microsoft.Extensions.Options;

internal sealed class AccessControlOptionsValidator : IValidateOptions<AccessControlOptions>
{
    public ValidateOptionsResult Validate(string? name, AccessControlOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return AccessControlRoleName.TryNormalize(options.Bootstrap.OwnerRoleName, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("AccessControl:Bootstrap:OwnerRoleName must be a lowercase role slug.");
    }
}
