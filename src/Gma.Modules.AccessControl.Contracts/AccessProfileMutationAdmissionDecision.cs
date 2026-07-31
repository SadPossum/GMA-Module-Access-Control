namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(AccessProfileMutationAdmissionDecisionJsonConverter))]
public enum AccessProfileMutationAdmissionDecision
{
    Unknown = 0,
    Allowed = 1,
    Denied = 2,
    Unavailable = 3
}
