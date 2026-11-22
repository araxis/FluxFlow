using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluxFlow.Environments;

/// <summary>An isolated edit of one startup file. Unmodeled JSON stays in the document.</summary>
public sealed class ServiceCollectionDocument
{
    private readonly string _source;
    private readonly JsonObject _original;
    private readonly JsonObject _value;
    public ServiceCollectionSchema Schema { get; }

    internal ServiceCollectionDocument(ServiceCollectionSchema schema, string source)
    {
        Schema = schema;
        _source = source;
        _original = ServiceCollectionSchema.ParseSource(source);
        foreach (var section in schema.Keys)
            if (_original.TryGetPropertyValue(section, out var node) && (node is not JsonArray array || array.Any(item => item is not JsonObject)))
                throw new ArgumentException($"{section} must be a list of objects. Use Advanced JSON to edit this document.");
        _value = _original.DeepClone().AsObject();
    }

    public IEnumerable<JsonObject> Rows(string section) =>
        _value[section] is JsonArray array ? array.Cast<JsonObject>() : [];

    public JsonObject Add(string section)
    {
        var row = Schema.Create(section);
        var array = (_value[section] ??= new JsonArray()).AsArray();
        array.Add(row);
        return row;
    }

    public void Remove(string section, JsonObject row)
    {
        if (_value[section] is JsonArray array && array.Remove(row) && array.Count == 0 && !_original.ContainsKey(section))
            _value.Remove(section);
    }

    public string Serialize() => JsonNode.DeepEquals(_value, _original)
        ? _source : (_source.StartsWith('\uFEFF') ? "\uFEFF" : "") + _value.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
