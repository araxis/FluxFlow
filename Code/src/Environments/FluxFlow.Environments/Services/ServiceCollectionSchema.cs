using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluxFlow.Environments;

/// <summary>Default JSON objects for an editor's named collections. New rows get independent copies.</summary>
public sealed class ServiceCollectionSchema(string schema, string? labels = null)
{
    private readonly JsonObject _templates = JsonNode.Parse(schema)!.AsObject();
    private readonly JsonObject? _labels = labels is null ? null : JsonNode.Parse(labels)!.AsObject();
    public IEnumerable<string> Keys => _templates.Select(pair => pair.Key);
    public string Label(string name) => _labels?[name]?.GetValue<string>()
        ?? char.ToUpperInvariant(name[0]) + name[1..].Replace('_', ' ');
    private JsonObject Template(string section) => _templates[section]?.AsObject()
        ?? throw new ArgumentException($"Unknown collection: {section}");
    public JsonObject Create(string name) => Template(name).DeepClone().AsObject();
    // Advanced JSON can contain sections or shapes that the visual schema doesn't describe.
    public static void ValidateSource(string source) => _ = ParseSource(source);
    internal static JsonObject ParseSource(string source) =>
        JsonNode.Parse(source.StartsWith('\uFEFF') ? source[1..] : source) as JsonObject
        ?? throw new ArgumentException("Settings must be a JSON object.");

    public ServiceCollectionDocument Open(string source) => new(this, source);

    public string Title(string section, JsonObject row) =>
        row[Template(section).First().Key] is JsonValue value && value.TryGetValue<string>(out var title) ? title : "New entry";

    public IEnumerable<DocumentField> Fields(string section, JsonObject row)
    {
        foreach (var (key, example) in Template(section))
        {
            if (example is not JsonValue) continue;
            var label = Label(key);
            var present = row.TryGetPropertyValue(key, out var value);
            var kind = value?.GetValueKind();
            switch (example.GetValueKind())
            {
                case JsonValueKind.String when !present || kind == JsonValueKind.String:
                    yield return new DocumentText(label, () => row[key]?.GetValue<string>() ?? "", text => row[key] = text ?? "");
                    break;
                case JsonValueKind.True or JsonValueKind.False when !present || kind is JsonValueKind.True or JsonValueKind.False:
                    yield return new DocumentFlag(label, () => row[key]?.GetValue<bool>() ?? false, flag => row[key] = flag);
                    break;
                default:
                    yield return new DocumentReadOnly(label, "Advanced value");
                    break;
            }
        }
    }
}
