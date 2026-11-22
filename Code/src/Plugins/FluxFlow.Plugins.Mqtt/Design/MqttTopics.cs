using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using FluxFlow.Components.Mqtt;
using FluxFlow.Configuration.Components.Mqtt;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Plugins.Mqtt;

public sealed class MqttTopics : IPropertyCollection
{
    public string Name => "Topics";
    public string ItemName => "topic";
    private static bool Manual(IReadOnlyDictionary<string, JsonElement> settings)
    {
        var mode = settings.FirstOrDefault(pair => pair.Key.Equals("AcknowledgementMode", StringComparison.OrdinalIgnoreCase)).Value;
        return Enum.TryParse<MqttAcknowledgementMode>(mode.ToString(), true, out var saved) && saved == MqttAcknowledgementMode.Manual;
    }
    public static int NewTopicQoS(IReadOnlyDictionary<string, JsonElement> settings) => Manual(settings) ? 1 : 0;
    public string Title(JsonElement row) => Topic(row);
    public string Detail(JsonElement row) => $"QoS {QoS(row)?.ToString() ?? "?"}";
    public IReadOnlyList<OptionEditorModel> Fields(JsonElement row, IReadOnlyDictionary<string, JsonElement> settings) => [
        OptionEditorModel.Create("Topic", "Topic filter", defaultValue: Topic(row)) with { ValidateText = ValidateFilter },
        OptionEditorModel.Create("QoS", "QoS", OptionEditorKind.Select, defaultValue: row.ValueKind == JsonValueKind.Undefined ? NewTopicQoS(settings) : QoS(row)) with
        { ValidateText = choice => choice == null ? "Select a QoS." : Manual(settings) && choice != "1" ? "Manual acknowledgement requires QoS 1 subscriptions." : null,
          Choices = [new() { Value = "0", DisplayName = "0 — At most once" }, new() { Value = "1", DisplayName = "1 — At least once" }, new() { Value = "2", DisplayName = "2 — Exactly once" }] }
    ];
    private static string? ValidateFilter(string? value)
    {
        try { new MqttTopic { Topic = value ?? "" }.Validate(); return null; }
        catch (ArgumentException error) { return error.Message; }
    }
    public JsonElement Edit(JsonElement row, IReadOnlyDictionary<string, string?> fields) =>
        Edit(row, fields["Topic"] ?? "", int.TryParse(fields["QoS"], out var qos) ? qos : throw new ArgumentException("Select a QoS."));
    public void Validate(JsonElement value, IReadOnlyDictionary<string, JsonElement> settings, string path)
    {
        if (value.ValueKind == JsonValueKind.Undefined) return;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartObject(); writer.WritePropertyName(Name); value.WriteTo(writer); writer.WriteEndObject(); }
        stream.Position = 0;
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
        try { MqttSubscriptions.Validate(Manual(settings) ? MqttAcknowledgementMode.Manual : MqttAcknowledgementMode.Automatic, 1, MqttSubscriptions.ReadTopics(config.GetSection(Name))); }
        catch (ArgumentException e) { throw new ArgumentException($"{path}: {e.Message}", e); }
    }

    public static string Key(IDictionary<string, JsonElement> configuration) =>
        configuration.Keys.FirstOrDefault(key => key.Equals("Topics", StringComparison.OrdinalIgnoreCase)) ?? "Topics";

    public JsonElement[] Read(IDictionary<string, JsonElement> configuration) =>
        !configuration.TryGetValue(Key(configuration), out var value) || value.ValueKind == JsonValueKind.Null ||
        (value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any()) ? []
        : value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [value];

    private static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        ? value.EnumerateObject().FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value : default;

    public static string Topic(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()!
        : Property(value, "Topic") is { ValueKind: JsonValueKind.String } topic ? topic.GetString()! : "";

    public static int? QoS(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return 0;
        var qos = Property(value, "QoS");
        if (qos.ValueKind == JsonValueKind.Undefined && value.ValueKind == JsonValueKind.Object) return 0;
        return int.TryParse(qos.ToString(), out var number) && number is >= 0 and <= 2 ? number : null;
    }

    public static JsonElement Edit(JsonElement original, string topic, int qos)
    {
        if (Topic(original) == topic && QoS(original) == qos) return original;
        if (original.ValueKind == JsonValueKind.String && qos == 0) return JsonSerializer.SerializeToElement(topic);
        var value = original.ValueKind == JsonValueKind.Object ? JsonNode.Parse(original.GetRawText())!.AsObject() : new JsonObject();
        if (Topic(original) != topic || original.ValueKind != JsonValueKind.Object)
            value[value.Select(pair => pair.Key).FirstOrDefault(key => key.Equals("Topic", StringComparison.OrdinalIgnoreCase)) ?? "Topic"] = topic;
        if (QoS(original) != qos || original.ValueKind != JsonValueKind.Object)
            value[value.Select(pair => pair.Key).FirstOrDefault(key => key.Equals("QoS", StringComparison.OrdinalIgnoreCase)) ?? "QoS"] = qos;
        return JsonSerializer.SerializeToElement(value);
    }

    public void Write(IDictionary<string, JsonElement> configuration, IReadOnlyList<JsonElement> topics)
    {
        var key = Key(configuration);
        if (topics.Count == 0)
        {
            // Omission means no initial subscriptions; an empty array can bind as an empty topic.
            configuration.Remove(key);
            return;
        }
        configuration[key] = topics.Count == 1 && configuration.TryGetValue(key, out var original) && original.ValueKind != JsonValueKind.Array
            ? topics[0] : JsonSerializer.SerializeToElement(topics);
    }
}
