namespace Gma.Modules.AccessControl.Application;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

public static class AccessControlOptionsValidation
{
    public static AccessControlOptions GetValidatedOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        AccessControlOptions options = new();
        configuration.GetSection(AccessControlOptions.SectionName).Bind(options);

        ValidateOptionsResult result = new AccessControlOptionsValidator().Validate(
            AccessControlOptions.SectionName,
            options);

        if (result.Failed)
        {
            throw new OptionsValidationException(
                AccessControlOptions.SectionName,
                typeof(AccessControlOptions),
                result.Failures);
        }

        return options;
    }
}
