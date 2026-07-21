namespace Gma.Modules.AccessControl.Domain.ValueObjects;

using Gma.Framework.Results;
using Gma.Modules.AccessControl.Domain.Errors;

public sealed record AccessProfileAssignmentScope
{
    public const int MaxLength = 1024;

    private AccessProfileAssignmentScope(string value) => this.Value = value;

    public string Value { get; }

    public static Result<AccessProfileAssignmentScope> Create(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        return candidate.Length is > 0 and <= MaxLength &&
               !string.Equals(candidate, AccessProfileOwnerScope.GlobalValue, StringComparison.OrdinalIgnoreCase) &&
               !candidate.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            ? Result.Success(new AccessProfileAssignmentScope(candidate))
            : Result.Failure<AccessProfileAssignmentScope>(AccessProfileDomainErrors.ScopeRequired);
    }
}
