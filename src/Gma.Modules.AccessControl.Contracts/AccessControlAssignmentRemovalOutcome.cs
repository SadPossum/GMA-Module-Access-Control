namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(AccessControlAssignmentRemovalOutcomeJsonConverter))]
public enum AccessControlAssignmentRemovalOutcome
{
    Unknown = 0,
    Removed = 1,
    NotFound = 2,
    LastOwnerProtected = 3
}
