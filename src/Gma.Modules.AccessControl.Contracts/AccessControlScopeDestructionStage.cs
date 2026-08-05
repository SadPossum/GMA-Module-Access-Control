namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(AccessControlContractKebabCaseEnumJsonConverter<AccessControlScopeDestructionStage>))]
public enum AccessControlScopeDestructionStage
{
    Unknown = 0,
    InboxMessages = 1,
    ProfileChanges = 2,
    ProfileAssignments = 3,
    RoleAssignments = 4,
    Profiles = 5,
    OrphanPrincipals = 6,
    Completed = 7
}
