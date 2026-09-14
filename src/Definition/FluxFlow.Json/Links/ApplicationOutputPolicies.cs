using System.Text.Json;
using FluxFlow.Json.Model;

namespace FluxFlow.Json.Links;

/// <summary>Reads explicit output intent without constructing components or links.</summary>
internal static class ApplicationOutputPolicies
{
    internal static bool IsPolicy(string name)
        => string.Equals(name, CanonicalApplicationProperties.DiscardUnmatched, StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> Read(IReadOnlyDictionary<string, JsonElement> properties,
        string policy, string component, Func<string, bool> isOutput)
    {
        IReadOnlyList<string> result = [];
        foreach (var (name, value) in properties)
        {
            if (!string.Equals(name, policy, StringComparison.OrdinalIgnoreCase)) continue;
            var location = $"{component}.{name}";
            if (name != policy) throw new JsonException($"{location}: use exact property name '{policy}'.");
            if (value.ValueKind != JsonValueKind.Array)
                throw new JsonException($"{location}: an array of output port names is required.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in value.EnumerateArray())
            {
                var port = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (string.IsNullOrWhiteSpace(port) || port != port.Trim() || port.Contains('.'))
                    throw new JsonException($"{location}: each entry must be a non-empty local output port name.");
                if (!isOutput(port)) throw new JsonException($"{location}: '{port}' is not an output port of '{component}'.");
                if (!names.Add(port)) throw new JsonException($"{location}: output '{port}' is declared more than once.");
            }
            result = names.Order(StringComparer.Ordinal).ToArray();
        }
        return result;
    }
}
