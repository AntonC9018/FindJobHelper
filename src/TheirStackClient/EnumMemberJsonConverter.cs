using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class EnumMemberConverterFactory : JsonConverterFactory
{
    private static readonly ConcurrentDictionary<Type, JsonConverter> Converters = new();

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsEnum;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return Converters.GetOrAdd(typeToConvert, (t) =>
        {
            var converterType = typeof(EnumMemberConverter<>).MakeGenericType(t);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        });
    }

    private sealed class EnumMemberConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        private readonly ConcurrentDictionary<T, string> _toString = new();
        private readonly ConcurrentDictionary<string, T> _fromString = new(StringComparer.OrdinalIgnoreCase);

        public EnumMemberConverter()
        {
            var type = typeof(T);

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var enumValue = (T)field.GetValue(null)!;
                var enumMemberAttr = field.GetCustomAttribute<EnumMemberAttribute>();
                var name = enumMemberAttr?.Value ?? field.Name;

                _toString[enumValue] = name;
                _fromString[name] = enumValue;
            }
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var enumText = reader.GetString()!;
            if (_fromString.TryGetValue(enumText, out var value))
            {
                return value;
            }

            throw new JsonException($"Unknown value '{enumText}' for enum type '{typeof(T)}'.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (_toString.TryGetValue(value, out var name))
            {
                writer.WriteStringValue(name);
            }
            else
            {
                writer.WriteStringValue(value.ToString());
            }
        }
    }
}
