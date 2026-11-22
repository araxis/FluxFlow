using System.Text.Json;

namespace FluxFlow.Studio.UI.Shared.Configuration;

internal static class JsonValueEntry
{
    public static JsonElement Read(JsonValueKind kind, string text, bool boolean)
    {
        if (kind == JsonValueKind.String) return JsonSerializer.SerializeToElement(text);
        if (kind == JsonValueKind.True) return JsonSerializer.SerializeToElement(boolean);
        if (kind == JsonValueKind.Null) return JsonSerializer.SerializeToElement<object?>(null);
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != kind) throw new ArgumentException($"Enter a JSON {kind.ToString().ToLowerInvariant()}.");
        return document.RootElement.Clone();
    }
}
