namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(AccessProfileMutationAdmissionOperationJsonConverter))]
public enum AccessProfileMutationAdmissionOperation
{
    Unknown = 0,
    CreateProfile = 1,
    UpdateProfile = 2,
    ArchiveProfile = 3,
    EnsureProfile = 4
}
