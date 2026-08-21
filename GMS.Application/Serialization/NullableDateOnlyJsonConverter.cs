namespace GMS.Application.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Treats JSON empty/whitespace strings as null so optional date inputs do not 400.
/// </summary>
public sealed class NullableDateOnlyJsonConverter : JsonConverter<DateOnly?>
{
    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            if (DateOnly.TryParse(raw, out var parsed))
                return parsed;
        }

        throw new JsonException("Hire date must be YYYY-MM-DD.");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
    {
        if (value is DateOnly d)
            writer.WriteStringValue(d.ToString("yyyy-MM-dd"));
        else
            writer.WriteNullValue();
    }
}
