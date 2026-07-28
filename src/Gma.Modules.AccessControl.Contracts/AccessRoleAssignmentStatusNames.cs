namespace Gma.Modules.AccessControl.Contracts;

public static class AccessRoleAssignmentStatusNames
{
    public static string ToWireName(AccessRoleAssignmentStatus status) =>
        status switch
        {
            AccessRoleAssignmentStatus.Active => "active",
            AccessRoleAssignmentStatus.Expired => "expired",
            AccessRoleAssignmentStatus.Revoked => "revoked",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Role-assignment status is invalid.")
        };

    public static bool TryParse(string? value, out AccessRoleAssignmentStatus status)
    {
        status = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "active" => AccessRoleAssignmentStatus.Active,
            "expired" => AccessRoleAssignmentStatus.Expired,
            "revoked" => AccessRoleAssignmentStatus.Revoked,
            _ => AccessRoleAssignmentStatus.Unknown
        };
        return status is not AccessRoleAssignmentStatus.Unknown;
    }
}
