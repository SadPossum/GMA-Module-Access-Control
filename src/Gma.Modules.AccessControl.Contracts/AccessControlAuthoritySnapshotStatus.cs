namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(AccessControlContractKebabCaseEnumJsonConverter<AccessControlAuthoritySnapshotStatus>))]
public enum AccessControlAuthoritySnapshotStatus
{
    Unknown = 0,
    Stable = 1,
    Stale = 2,
    Closed = 3,
    Invalid = 4,
    Unavailable = 5
}
