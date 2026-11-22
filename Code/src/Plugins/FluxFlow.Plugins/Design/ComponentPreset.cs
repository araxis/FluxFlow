using System.Text.Json;
using FluxFlow.Authoring;

namespace FluxFlow.Plugins;

/// <summary>A palette preset for one setting. Imported nodes outside the preset expose that setting for editing.</summary>
public sealed record ComponentPreset(OptionEditorModel Field, string Value, string CustomDisplayName, string CustomSummary)
{
    internal bool Matches(IReadOnlyDictionary<string, JsonElement> settings)
    {
        var value = settings.FirstOrDefault(entry => entry.Key.Equals(Field.Name, StringComparison.OrdinalIgnoreCase)).Value;
        return value.ValueKind == JsonValueKind.String && value.GetString() == Value;
    }
}
