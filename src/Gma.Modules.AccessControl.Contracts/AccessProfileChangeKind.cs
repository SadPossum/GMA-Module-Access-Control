namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(AccessProfileChangeKindJsonConverter))]
public enum AccessProfileChangeKind
{
    Unknown = 0,
    Created = 1,
    Updated = 2,
    Archived = 3,
    Assigned = 4,
    Unassigned = 5
}
