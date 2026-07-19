namespace Gma.Modules.AccessControl.Domain.ValueObjects;

using Gma.Framework.Results;
using Gma.Modules.AccessControl.Domain.Errors;

public sealed record AccessProfileOwnerScope
{
    public const int MaxLength = 1024;
    public const string GlobalValue = "global";

    private AccessProfileOwnerScope(string value) => this.Value = value;

    public string Value { get; }

    public static Result<AccessProfileOwnerScope> Create(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        return candidate.Length is > 0 and <= MaxLength &&
               !string.Equals(candidate, GlobalValue, StringComparison.OrdinalIgnoreCase) &&
               !candidate.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            ? Result.Success(new AccessProfileOwnerScope(candidate))
            : Result.Failure<AccessProfileOwnerScope>(AccessProfileDomainErrors.ScopeRequired);
    }
}
