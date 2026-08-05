namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(AccessControlContractKebabCaseEnumJsonConverter<AccessControlScopeStatus>))]
public enum AccessControlScopeStatus
{
    Unknown = 0,
    Invalid = 1,
    Missing = 2,
    Open = 3,
    Closed = 4
}
