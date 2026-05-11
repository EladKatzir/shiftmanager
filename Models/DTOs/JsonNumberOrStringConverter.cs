using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShiftManager.Models.DTOs;

/// <summary>
/// Reads any JSON scalar (string, number, boolean, null) into a CLR <c>string</c>.
///
/// Defensive measure for DTOs deserialising from external APIs whose contract isn't
/// strictly enforced (e.g. Griffin/ADFS may emit a UniqueID as either <c>"7108elad@8200"</c>
/// or <c>7108</c> depending on deployment). Without this converter, the default
/// System.Text.Json string-property reader throws <c>JsonException</c> on a JSON number
/// — and the whole deserialisation aborts, blocking login.
///
/// Numbers use <c>InvariantCulture</c> so locale settings (Hebrew RTL, decimal-comma)
/// do not change the captured value.
///
/// Objects and arrays are NOT supported here — apply this converter only to fields
/// whose semantic value is a scalar string. For fields that legitimately may be an
/// array (e.g. JWT <c>aud</c>), use <c>JsonElement?</c> instead.
/// </summary>
public sealed class JsonNumberOrStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number => reader.TryGetInt64(out var l)
                ? l.ToString(CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => string.Empty,
            _ => throw new JsonException(
                $"JsonNumberOrStringConverter cannot convert token of type {reader.TokenType} to string. "
                + "Only String, Number, Boolean, and Null are supported. "
                + "If the field can be an array or object, use JsonElement? instead.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
