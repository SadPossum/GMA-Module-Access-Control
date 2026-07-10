namespace Gma.Modules.AccessControl.Application;

using System.Diagnostics.CodeAnalysis;
using Gma.Framework.Naming;

public static class AccessControlRoleName
{
    public const int MaxLength = 64;

    public static string Normalize(string roleName)
    {
        if (TryNormalize(roleName, out string? normalized))
        {
            return normalized;
        }

        throw new ArgumentException(
            $"Access-control role name must be a lowercase kebab-case slug, {MaxLength} characters or fewer.",
            nameof(roleName));
    }

    public static bool TryNormalize(string? roleName, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(roleName))
        {
            return false;
        }

        string candidate = roleName.Trim().ToLowerInvariant();
        if (candidate.Length > MaxLength || !SharedNameSegments.IsKebabSegment(candidate))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }
}
