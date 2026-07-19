namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class AccessProfileChangeKindJsonConverter : JsonConverter<AccessProfileChangeKind>
{
    public override AccessProfileChangeKind Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException("Access-profile change kind must be a string.");
        }

        return AccessProfileChangeKindNames.TryParse(reader.GetString(), out AccessProfileChangeKind kind)
            ? kind
            : throw new JsonException("Access-profile change kind is invalid.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        AccessProfileChangeKind value,
        JsonSerializerOptions options)
    {
        try
        {
            writer.WriteStringValue(AccessProfileChangeKindNames.ToWireName(value));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new JsonException("Access-profile change kind is invalid.", exception);
        }
    }
}
