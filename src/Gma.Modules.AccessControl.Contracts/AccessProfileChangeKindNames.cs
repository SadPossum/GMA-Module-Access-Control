namespace Gma.Modules.AccessControl.Contracts;

public static class AccessProfileChangeKindNames
{
    public static string ToWireName(AccessProfileChangeKind kind) =>
        kind switch
        {
            AccessProfileChangeKind.Created => "created",
            AccessProfileChangeKind.Updated => "updated",
            AccessProfileChangeKind.Archived => "archived",
            AccessProfileChangeKind.Assigned => "assigned",
            AccessProfileChangeKind.Unassigned => "unassigned",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Access-profile change kind is invalid.")
        };

    public static bool TryParse(string? value, out AccessProfileChangeKind kind)
    {
        kind = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "created" => AccessProfileChangeKind.Created,
            "updated" => AccessProfileChangeKind.Updated,
            "archived" => AccessProfileChangeKind.Archived,
            "assigned" => AccessProfileChangeKind.Assigned,
            "unassigned" => AccessProfileChangeKind.Unassigned,
            _ => AccessProfileChangeKind.Unknown
        };
        return kind is not AccessProfileChangeKind.Unknown;
    }
}
