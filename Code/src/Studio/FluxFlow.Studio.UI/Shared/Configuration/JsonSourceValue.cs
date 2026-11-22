using System.Text.Json;
using FluxFlow.Configuration.Sources;

namespace FluxFlow.Studio.UI.Shared.Configuration;

/// <summary>Classifies stored JSON sources without opening runner files.</summary>
public sealed record JsonSourceValue(string Original, bool IsFile, string Content)
{
    public static JsonSourceValue Parse(string? source)
    {
        var original = source ?? "";
        var text = original.Trim();
        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return new(original, true, text[5..]);
        if (text.StartsWith("inline:", StringComparison.OrdinalIgnoreCase)) return new(original, false, text[7..]);
        if (text.Length == 0) return new(original, false, text);
        try { using var json = JsonDocument.Parse(text); return new(original, false, text); }
        catch (JsonException) { return new(original, text[0] is not ('{' or '[' or '"'), text); }
    }

    public string Save(bool file, string content, bool arrayOnly)
    {
        if (file) ArgumentException.ThrowIfNullOrWhiteSpace(content, "File path");
        else
        {
            // The explicit inline prefix uses the native parser without allowing file access.
            var json = new JsonSource("inline:" + content).Read(AppContext.BaseDirectory);
            if (arrayOnly && json.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Enter a JSON array of values.");
        }
        return file == IsFile && content == Content ? Original : (file ? "file:" : "inline:") + content;
    }
}
