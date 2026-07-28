namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class AccessRoleAssignmentStatusJsonConverter
    : JsonConverter<AccessRoleAssignmentStatus>
{
    public override AccessRoleAssignmentStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException("Role-assignment status must be a string.");
        }

        return AccessRoleAssignmentStatusNames.TryParse(
            reader.GetString(),
            out AccessRoleAssignmentStatus status)
            ? status
            : throw new JsonException("Role-assignment status is invalid.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        AccessRoleAssignmentStatus value,
        JsonSerializerOptions options)
    {
        try
        {
            writer.WriteStringValue(AccessRoleAssignmentStatusNames.ToWireName(value));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new JsonException("Role-assignment status is invalid.", exception);
        }
    }
}
