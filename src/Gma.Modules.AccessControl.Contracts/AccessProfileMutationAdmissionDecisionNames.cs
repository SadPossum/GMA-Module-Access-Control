namespace Gma.Modules.AccessControl.Contracts;

public static class AccessProfileMutationAdmissionDecisionNames
{
    public static string ToWireName(
        AccessProfileMutationAdmissionDecision decision) =>
        decision switch
        {
            AccessProfileMutationAdmissionDecision.Allowed => "allowed",
            AccessProfileMutationAdmissionDecision.Denied => "denied",
            AccessProfileMutationAdmissionDecision.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(decision),
                decision,
                "Access-profile mutation admission decision is invalid.")
        };

    public static bool TryParse(
        string? value,
        out AccessProfileMutationAdmissionDecision decision)
    {
        decision = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "allowed" => AccessProfileMutationAdmissionDecision.Allowed,
            "denied" => AccessProfileMutationAdmissionDecision.Denied,
            "unavailable" => AccessProfileMutationAdmissionDecision.Unavailable,
            _ => AccessProfileMutationAdmissionDecision.Unknown
        };
        return decision is not AccessProfileMutationAdmissionDecision.Unknown;
    }
}
