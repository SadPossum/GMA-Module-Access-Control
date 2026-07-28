namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class AccessRoleAssignmentLifecycleStageJsonConverter
    : JsonConverter<AccessRoleAssignmentLifecycleStage>
{
    public override AccessRoleAssignmentLifecycleStage Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException("Role-assignment lifecycle stage must be a string.");
        }

        return AccessRoleAssignmentLifecycleStageNames.TryParse(
            reader.GetString(),
            out AccessRoleAssignmentLifecycleStage stage)
            ? stage
            : throw new JsonException("Role-assignment lifecycle stage is invalid.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        AccessRoleAssignmentLifecycleStage value,
        JsonSerializerOptions options)
    {
        try
        {
            writer.WriteStringValue(AccessRoleAssignmentLifecycleStageNames.ToWireName(value));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new JsonException("Role-assignment lifecycle stage is invalid.", exception);
        }
    }
}
