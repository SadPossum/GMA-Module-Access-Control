namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(AccessRoleAssignmentStatusJsonConverter))]
public enum AccessRoleAssignmentStatus
{
    Unknown = 0,
    Active = 1,
    Expired = 2,
    Revoked = 3
}
