namespace Gma.Modules.AccessControl.Domain.ValueObjects;

using Gma.Framework.Results;
using Gma.Modules.AccessControl.Domain.Errors;

public sealed record AccessProfileDisplayName
{
    public const int MaxLength = 100;

    private AccessProfileDisplayName(string value) => this.Value = value;

    public string Value { get; }

    public static Result<AccessProfileDisplayName> Create(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        return candidate.Length is > 0 and <= MaxLength && !candidate.Any(char.IsControl)
            ? Result.Success(new AccessProfileDisplayName(candidate))
            : Result.Failure<AccessProfileDisplayName>(AccessProfileDomainErrors.DisplayNameInvalid);
    }
}
