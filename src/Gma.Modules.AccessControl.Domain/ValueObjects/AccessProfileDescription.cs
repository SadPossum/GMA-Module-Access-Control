namespace Gma.Modules.AccessControl.Domain.ValueObjects;

using Gma.Framework.Results;
using Gma.Modules.AccessControl.Domain.Errors;

public sealed record AccessProfileDescription
{
    public const int MaxLength = 500;

    private AccessProfileDescription(string value) => this.Value = value;

    public string Value { get; }

    public static Result<AccessProfileDescription> Create(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        return candidate.Length <= MaxLength && !candidate.Any(char.IsControl)
            ? Result.Success(new AccessProfileDescription(candidate))
            : Result.Failure<AccessProfileDescription>(AccessProfileDomainErrors.DescriptionInvalid);
    }
}
