namespace Gma.Modules.AccessControl.Domain.ValueObjects;

using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Domain.Errors;

public sealed record AccessProfileKey
{
    public const int MaxLength = 64;

    private AccessProfileKey(string value) => this.Value = value;

    public string Value { get; }

    public static Result<AccessProfileKey> Create(string? value)
    {
        string candidate = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return candidate.Length is > 0 and <= MaxLength && SharedNameSegments.IsKebabSegment(candidate)
            ? Result.Success(new AccessProfileKey(candidate))
            : Result.Failure<AccessProfileKey>(AccessProfileDomainErrors.KeyInvalid);
    }
}
