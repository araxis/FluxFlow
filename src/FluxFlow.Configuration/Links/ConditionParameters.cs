using System.Collections.Immutable;
using System.Text.Json;

namespace FluxFlow.Configuration.Links;

internal static class ConditionParameters
{
    internal static IReadOnlyDictionary<string, JsonElement> Copy(
        IEnumerable<KeyValuePair<string, JsonElement>>? parameters)
    {
        var values = ImmutableDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters ?? [])
        {
            if (string.IsNullOrWhiteSpace(name) || !(char.IsLetter(name[0]) || name[0] == '_') ||
                name.Any(static character => !char.IsLetterOrDigit(character) && character != '_'))
                throw new ArgumentException($"Condition parameter '{name}' must be an identifier.", nameof(parameters));
            if (name is "input" or "payload" or "message")
                throw new ArgumentException($"Condition parameter '{name}' is reserved.", nameof(parameters));
            if (value.ValueKind == JsonValueKind.Undefined)
                throw new ArgumentException($"Condition parameter '{name}' cannot be undefined.", nameof(parameters));
            if (!values.TryAdd(name, value.Clone()))
                throw new ArgumentException($"Duplicate condition parameter '{name}'.", nameof(parameters));
        }
        return values.ToImmutable();
    }

    internal static object? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt32(out var number) => number,
        JsonValueKind.Number when value.TryGetInt64(out var number) => number,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.Number => value.GetDouble(),
        _ => value.Clone()
    };
}
