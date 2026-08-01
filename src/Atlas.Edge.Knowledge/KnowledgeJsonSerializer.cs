using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atlas.Edge.Knowledge;

public static class KnowledgeJsonSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public static string Serialize(KnowledgeRecord record)
    {
        KnowledgeRecordValidator.Validate(record);
        return JsonSerializer.Serialize(record, SerializerOptions);
    }

    public static KnowledgeRecord Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var record = JsonSerializer.Deserialize<KnowledgeRecord>(json, SerializerOptions) ??
            throw new JsonException("Knowledge record JSON did not contain a record.");
        KnowledgeRecordValidator.Validate(record);
        return record;
    }

    internal static JsonSerializerOptions Options => SerializerOptions;

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new TimestampJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class TimestampJsonConverter : JsonConverter<Timestamp>
    {
        public override Timestamp Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String ||
                !reader.TryGetDateTimeOffset(out var value))
            {
                throw new JsonException("A knowledge timestamp must be a valid ISO 8601 date and time.");
            }

            return new Timestamp(value);
        }

        public override void Write(
            Utf8JsonWriter writer,
            Timestamp value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }
}
