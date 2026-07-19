namespace Gma.Modules.AccessControl.Contracts;

public static class AccessProfileStatusNames
{
    public static string ToWireName(AccessProfileStatus status) =>
        status switch
        {
            AccessProfileStatus.Active => "active",
            AccessProfileStatus.Archived => "archived",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Access-profile status is invalid.")
        };

    public static bool TryParse(string? value, out AccessProfileStatus status)
    {
        status = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "active" => AccessProfileStatus.Active,
            "archived" => AccessProfileStatus.Archived,
            _ => AccessProfileStatus.Unknown
        };
        return status is not AccessProfileStatus.Unknown;
    }
}
