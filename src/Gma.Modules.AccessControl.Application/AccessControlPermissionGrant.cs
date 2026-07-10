namespace Gma.Modules.AccessControl.Application;

using System.Diagnostics.CodeAnalysis;
using Gma.Framework.Permissions;

public static class AccessControlPermissionGrant
{
    public const string OwnerWildcard = "*";

    public static string Normalize(string permissionCode)
    {
        if (TryNormalize(permissionCode, out string? normalized))
        {
            return normalized;
        }

        throw new ArgumentException("Permission grant must be a permission code or the owner wildcard.", nameof(permissionCode));
    }

    public static bool TryNormalize(string? permissionCode, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(permissionCode))
        {
            return false;
        }

        string candidate = permissionCode.Trim().ToLowerInvariant();
        if (candidate == OwnerWildcard)
        {
            normalized = OwnerWildcard;
            return true;
        }

        if (!PermissionCode.TryCreate(candidate, out PermissionCode? permission))
        {
            return false;
        }

        normalized = permission.Value;
        return true;
    }
}
