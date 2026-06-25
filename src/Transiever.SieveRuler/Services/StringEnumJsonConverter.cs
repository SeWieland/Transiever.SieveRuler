
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Transiever.SieveRuler.Services;

internal sealed class StringEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var result))
            return result;

        throw new JsonException($"'{value}' is not a valid {typeof(TEnum).Name} value.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        TEnum value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
