namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(AccessControlContractKebabCaseEnumJsonConverter<AccessControlScopeExportStatus>))]
public enum AccessControlScopeExportStatus
{
    Unknown = 0,
    Invalid = 1,
    Completed = 2,
    Missing = 3,
    Closed = 4,
    Stale = 5
}
