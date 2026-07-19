namespace Gma.Modules.AccessControl.Domain.Enums;

public static class AccessProfileChangeKindNames
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Archived = "archived";
    public const string Assigned = "assigned";
    public const string Unassigned = "unassigned";

    public static string GetName(AccessProfileChangeKind kind) => kind switch
    {
        AccessProfileChangeKind.Created => Created,
        AccessProfileChangeKind.Updated => Updated,
        AccessProfileChangeKind.Archived => Archived,
        AccessProfileChangeKind.Assigned => Assigned,
        AccessProfileChangeKind.Unassigned => Unassigned,
        _ => throw new ArgumentException("Access-profile change kind must be defined and non-unknown.", nameof(kind))
    };
}
