using System.Text.Json;
using FluxFlow.Authoring;

namespace FluxFlow.Plugins;

/// <summary>Each key in a configured expression map contributes a named output port.</summary>
public sealed record NamedExpressionOutputs(string Property, string EntryLabel, string Summary, IReadOnlyList<string> ReservedNames)
{
    // The name must be one segment in both a port address (.) and a configuration path (:).
    public static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length == name.AsSpan().Trim().Length &&
        name.AsSpan().IndexOfAny('.', ':') < 0;

    public PaletteItemModel Apply(PaletteItemModel palette, IReadOnlyDictionary<string, JsonElement> configuration)
    {
        var map = configuration.FirstOrDefault(item => item.Key.Equals(Property, StringComparison.OrdinalIgnoreCase)).Value;
        if (map.ValueKind != JsonValueKind.Object) return palette;
        return palette with { Outputs = [.. palette.Outputs, .. map.EnumerateObject()
            .Where(item => IsValidName(item.Name) && !ReservedNames.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
            .Select(item => new PortModel { Name = item.Name, Kind = PortKind.Output, Summary = Summary })] };
    }
}
