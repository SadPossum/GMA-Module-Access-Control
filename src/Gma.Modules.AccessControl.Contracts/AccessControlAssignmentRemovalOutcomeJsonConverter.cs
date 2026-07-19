namespace Gma.Modules.AccessControl.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class AccessControlAssignmentRemovalOutcomeJsonConverter
    : JsonConverter<AccessControlAssignmentRemovalOutcome>
{
    public override AccessControlAssignmentRemovalOutcome Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException("Assignment removal outcome must be a string.");
        }

        return AccessControlAssignmentRemovalOutcomeNames.TryParse(
            reader.GetString(), out AccessControlAssignmentRemovalOutcome outcome)
            ? outcome
            : throw new JsonException("Assignment removal outcome is invalid.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        AccessControlAssignmentRemovalOutcome value,
        JsonSerializerOptions options)
    {
        try
        {
            writer.WriteStringValue(AccessControlAssignmentRemovalOutcomeNames.ToWireName(value));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new JsonException("Assignment removal outcome is invalid.", exception);
        }
    }
}
