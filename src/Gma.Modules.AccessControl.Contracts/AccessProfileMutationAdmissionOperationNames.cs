namespace Gma.Modules.AccessControl.Contracts;

public static class AccessProfileMutationAdmissionOperationNames
{
    public static string ToWireName(
        AccessProfileMutationAdmissionOperation operation) =>
        operation switch
        {
            AccessProfileMutationAdmissionOperation.CreateProfile =>
                "create-profile",
            AccessProfileMutationAdmissionOperation.UpdateProfile =>
                "update-profile",
            AccessProfileMutationAdmissionOperation.ArchiveProfile =>
                "archive-profile",
            AccessProfileMutationAdmissionOperation.EnsureProfile =>
                "ensure-profile",
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation,
                "Access-profile mutation admission operation is invalid.")
        };

    public static bool TryParse(
        string? value,
        out AccessProfileMutationAdmissionOperation operation)
    {
        operation = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "create-profile" =>
                AccessProfileMutationAdmissionOperation.CreateProfile,
            "update-profile" =>
                AccessProfileMutationAdmissionOperation.UpdateProfile,
            "archive-profile" =>
                AccessProfileMutationAdmissionOperation.ArchiveProfile,
            "ensure-profile" =>
                AccessProfileMutationAdmissionOperation.EnsureProfile,
            _ => AccessProfileMutationAdmissionOperation.Unknown
        };
        return operation is not AccessProfileMutationAdmissionOperation.Unknown;
    }
}
