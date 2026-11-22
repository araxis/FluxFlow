using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluxFlow.Environments;

/// <summary>An isolated edit of a provider document containing one object or an ordered list of objects.</summary>
public sealed class ServiceItemsDraft(ServiceDocument schema, string originalText)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private readonly List<JsonObject> _items = [];
    private readonly JsonNode? _original = ReadOriginal(schema, originalText);
    private JsonNode? _baseline;
    private string _baselineText = originalText;
    private bool _objectWhenSingle;
    public int Count => _items.Count;

    private static JsonNode? ReadOriginal(ServiceDocument schema, string text)
    {
        // Invalid saved mappings have no baseline, but can still be repaired in Advanced JSON.
        try { return Parse(schema, text); }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    private static JsonNode Parse(ServiceDocument schema, string text)
    {
        schema.Validate(text);
        var root = JsonNode.Parse(text);
        return root is JsonObject || root is JsonArray array && array.All(n => n is JsonObject)
            ? root : throw new ArgumentException("Expected an object or an array of objects.");
    }

    // A dialog can still show and repair invalid original text if opening the visual draft fails.
    public bool LoadVisual(string text)
    {
        var root = text == originalText && _original is not null ? _original : Parse(schema, text);
        var changed = _baseline is null || !JsonNode.DeepEquals(root, CurrentRoot());
        _baseline = root;
        _baselineText = text;
        // Keep the current selection and preferred shape if Advanced JSON only changed formatting.
        if (!changed) return false;
        _objectWhenSingle = root is JsonObject;
        _items.Clear();
        if (root is JsonArray rows) _items.AddRange(rows.Cast<JsonObject>());
        else _items.Add(root.AsObject());
        return true;
    }

    public IServiceDocumentItem Open(int index) => schema.Open((JsonObject)_items[index].DeepClone());
    public void Apply(int index, IServiceDocumentItem item) => _items[index] = item.Apply();
    public void Add() => _items.Add(schema.Create());
    public void RemoveAt(int index) => _items.RemoveAt(index);

    public string Serialize()
    {
        if (_baseline is null) throw new InvalidOperationException("Load a valid document before saving visual edits.");
        var root = CurrentRoot();
        if (JsonNode.DeepEquals(_original, root)) return originalText;
        return JsonNode.DeepEquals(_baseline, root) ? _baselineText : root.ToJsonString(Indented);
    }

    private JsonNode CurrentRoot() => _objectWhenSingle && Count == 1 ? _items[0]
        : new JsonArray(_items.Select(item => item.DeepClone()).ToArray());

    public string SaveAdvanced(string text)
    {
        var root = Parse(schema, text);
        return JsonNode.DeepEquals(_original, root) ? originalText : text;
    }
}
