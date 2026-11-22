using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluxFlow.Studio.UI.Features.Designer.Components.Expressions;

internal static class SampleHeaderPath
{
    public static JsonObject Add(JsonObject headers, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        if (parts.Any(string.IsNullOrWhiteSpace)) throw new JsonException("Enter a key or a dotted path with no empty segments.");
        // Work on a copy so a conflicting path never changes the sample.
        var result = (JsonObject)headers.DeepClone();
        var parent = result;
        foreach (var part in parts[..^1])
        {
            if (!parent.TryGetPropertyValue(part, out var child)) parent.Add(part, child = new JsonObject());
            parent = child as JsonObject ?? throw new JsonException($"'{part}' already exists and is not an object.");
        }
        if (parent.ContainsKey(parts[^1])) throw new JsonException($"Header '{path}' already exists. Edit its value in JSON.");
        parent.Add(parts[^1], value?.DeepClone());
        return result;
    }
}
