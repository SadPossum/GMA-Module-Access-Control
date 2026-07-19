namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class AccessProfileStatusJsonConverter : JsonConverter<AccessProfileStatus>
{
    public override AccessProfileStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException("Access-profile status must be a string.");
        }

        return AccessProfileStatusNames.TryParse(reader.GetString(), out AccessProfileStatus status)
            ? status
            : throw new JsonException("Access-profile status is invalid.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        AccessProfileStatus value,
        JsonSerializerOptions options)
    {
        try
        {
            writer.WriteStringValue(AccessProfileStatusNames.ToWireName(value));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new JsonException("Access-profile status is invalid.", exception);
        }
    }
}
