namespace Gma.Modules.AccessControl.Contracts;

public static class AccessControlAssignmentRemovalOutcomeNames
{
    public static string ToWireName(AccessControlAssignmentRemovalOutcome outcome) =>
        outcome switch
        {
            AccessControlAssignmentRemovalOutcome.Removed => "removed",
            AccessControlAssignmentRemovalOutcome.NotFound => "not-found",
            AccessControlAssignmentRemovalOutcome.LastOwnerProtected => "last-owner-protected",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Assignment removal outcome is invalid.")
        };

    public static bool TryParse(string? value, out AccessControlAssignmentRemovalOutcome outcome)
    {
        outcome = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "removed" => AccessControlAssignmentRemovalOutcome.Removed,
            "not-found" => AccessControlAssignmentRemovalOutcome.NotFound,
            "last-owner-protected" => AccessControlAssignmentRemovalOutcome.LastOwnerProtected,
            _ => AccessControlAssignmentRemovalOutcome.Unknown
        };
        return outcome is not AccessControlAssignmentRemovalOutcome.Unknown;
    }
}
