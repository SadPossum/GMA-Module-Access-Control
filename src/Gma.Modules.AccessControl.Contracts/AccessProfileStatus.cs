namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(AccessProfileStatusJsonConverter))]
public enum AccessProfileStatus
{
    Unknown = 0,
    Active = 1,
    Archived = 2
}
