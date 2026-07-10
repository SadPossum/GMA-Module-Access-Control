namespace Gma.Modules.AccessControl.AdminApi;

using System.Text.Json;
using System.Text.Json.Serialization;
using Gma.Framework.AccessControl;

public sealed class AccessScopeJsonConverter : JsonConverter<AccessScope>
{
    public override AccessScope Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !AccessScope.TryParse(reader.GetString(), out AccessScope? scope))
        {
            throw new JsonException("Access scope must be 'global' or slash-separated name:value segments.");
        }

        return scope;
    }

    public override void Write(
        Utf8JsonWriter writer,
        AccessScope value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.Value);
    }
}
