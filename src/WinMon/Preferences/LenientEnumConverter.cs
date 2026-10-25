using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMon.Preferences;

/// <summary>
/// Stores enums by name, and reads a name this build does not define as an undefined value, so validation can
/// replace that one setting instead of the whole file failing to load. A newer build may add names.
/// </summary>
internal sealed class LenientEnumConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsEnum;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class Converter<T> : JsonConverter<T> where T : struct, Enum
    {
        private static readonly T Unknown = (T)Enum.ToObject(typeof(T), -1);

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
                return reader.TryGetInt32(out var number) ? (T)Enum.ToObject(typeof(T), number) : Unknown;
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected a {typeof(T).Name} name.");
            var text = reader.GetString();
            foreach (var name in Enum.GetNames<T>())
                if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse<T>(name);
            return Unknown;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
