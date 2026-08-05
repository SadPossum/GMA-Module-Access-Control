namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(AccessControlContractKebabCaseEnumJsonConverter<AccessControlScopeExportStore>))]
public enum AccessControlScopeExportStore
{
    Unknown = 0,
    RoleAssignments = 1,
    Profiles = 2,
    ProfileAssignments = 3,
    ProfileChanges = 4
}
