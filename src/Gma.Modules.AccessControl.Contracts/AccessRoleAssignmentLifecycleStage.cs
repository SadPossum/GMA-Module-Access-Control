namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(AccessRoleAssignmentLifecycleStageJsonConverter))]
public enum AccessRoleAssignmentLifecycleStage
{
    Unknown = 0,
    Requested = 1,
    Granted = 2,
    Denied = 3,
    Revoked = 4
}
