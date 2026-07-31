namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class AccessProfileMutationAdmissionDecisionJsonConverter
    : JsonConverter<AccessProfileMutationAdmissionDecision>
{
    public override AccessProfileMutationAdmissionDecision Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException(
                "Access-profile mutation admission decision must be a string.");
        }

        return AccessProfileMutationAdmissionDecisionNames.TryParse(
            reader.GetString(),
            out AccessProfileMutationAdmissionDecision decision)
            ? decision
            : throw new JsonException(
                "Access-profile mutation admission decision is invalid.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        AccessProfileMutationAdmissionDecision value,
        JsonSerializerOptions options)
    {
        try
        {
            writer.WriteStringValue(
                AccessProfileMutationAdmissionDecisionNames.ToWireName(value));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new JsonException(
                "Access-profile mutation admission decision is invalid.",
                exception);
        }
    }
}
