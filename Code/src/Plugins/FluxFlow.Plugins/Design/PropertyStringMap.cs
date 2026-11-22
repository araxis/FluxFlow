using System.Text.Json;

namespace FluxFlow.Plugins;

public sealed record StringMapEntry(string Name, string Value);

public sealed record PropertyStringMap(string Name, string Label, string EntryLabel)
{
    public StringComparer Comparer { get; init; } = StringComparer.Ordinal;
    public bool SecretValues { get; init; }
    public Func<string, string, string?>? Validate { get; init; }
    public Func<JsonElement, StringMapEntry[]?>? ReadEntries { get; init; }
    public Func<JsonElement, IReadOnlyList<StringMapEntry>, JsonElement>? WriteEntries { get; init; }
    public Action<JsonElement>? ValidateJson { get; init; }
}
