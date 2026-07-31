namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class AccessProfileMutationAdmissionOperationJsonConverter
    : JsonConverter<AccessProfileMutationAdmissionOperation>
{
    public override AccessProfileMutationAdmissionOperation Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException(
                "Access-profile mutation admission operation must be a string.");
        }

        return AccessProfileMutationAdmissionOperationNames.TryParse(
            reader.GetString(),
            out AccessProfileMutationAdmissionOperation operation)
            ? operation
            : throw new JsonException(
                "Access-profile mutation admission operation is invalid.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        AccessProfileMutationAdmissionOperation value,
        JsonSerializerOptions options)
    {
        try
        {
            writer.WriteStringValue(
                AccessProfileMutationAdmissionOperationNames.ToWireName(value));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new JsonException(
                "Access-profile mutation admission operation is invalid.",
                exception);
        }
    }
}
