namespace Gma.Modules.AccessControl.Contracts;

public static class AccessRoleAssignmentLifecycleStageNames
{
    public static string ToWireName(AccessRoleAssignmentLifecycleStage stage) =>
        stage switch
        {
            AccessRoleAssignmentLifecycleStage.Requested => "requested",
            AccessRoleAssignmentLifecycleStage.Granted => "granted",
            AccessRoleAssignmentLifecycleStage.Denied => "denied",
            AccessRoleAssignmentLifecycleStage.Revoked => "revoked",
            _ => throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "Role-assignment lifecycle stage is invalid.")
        };

    public static bool TryParse(string? value, out AccessRoleAssignmentLifecycleStage stage)
    {
        stage = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "requested" => AccessRoleAssignmentLifecycleStage.Requested,
            "granted" => AccessRoleAssignmentLifecycleStage.Granted,
            "denied" => AccessRoleAssignmentLifecycleStage.Denied,
            "revoked" => AccessRoleAssignmentLifecycleStage.Revoked,
            _ => AccessRoleAssignmentLifecycleStage.Unknown
        };
        return stage is not AccessRoleAssignmentLifecycleStage.Unknown;
    }
}
