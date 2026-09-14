using System.Text.Json;

namespace FluxFlow.Json.Model;

internal static class CanonicalApplicationProperties
{
    internal static string Normalize(string name)
    {
        foreach (var known in new[] { Resources, Workflows, ExpressionEngine, Type, LinkPort,
                     LinkCondition, LinkParameters, LinkDelivery, LinkFallback, DiscardUnmatched, Processing })
            if (string.Equals(name, known, StringComparison.OrdinalIgnoreCase)) return known;
        return name;
    }

    internal static string NormalizeWorkflowProperty(string name)
        => string.Equals(name, ExpressionEngine, StringComparison.OrdinalIgnoreCase) ? ExpressionEngine
            : string.Equals(name, Resources, StringComparison.OrdinalIgnoreCase) ? Resources : name;

    internal static string NormalizeComponentProperty(string name)
    {
        foreach (var known in new[] { Type, Processing, DiscardUnmatched, "Input", "Output", "Events" })
            if (string.Equals(name, known, StringComparison.OrdinalIgnoreCase)) return known;
        return name;
    }

    internal const string ExpressionEngine = "ExpressionEngine";
    internal const string LinkParameters = "Parameters";
    internal const string Resources = "Resources";
    internal const string Workflows = "Workflows";
    internal const string Type = "Type";
    internal const string Processing = "Processing";
    internal const string Name = "Name";
    internal const string LegacyConfiguration = "Configuration";
    internal const string LinkPort = "Port";
    internal const string LinkCondition = "Condition";
    internal const string LinkDelivery = "Delivery";
    internal const string LinkFallback = "Fallback";
    internal const string DiscardUnmatched = "DiscardUnmatched";

    internal static bool IsLegacyComponentWrapper(string name)
        => string.Equals(name, LegacyConfiguration, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name, Resources, StringComparison.OrdinalIgnoreCase);

    internal static bool ContainsIgnoreCase<TValue>(
        IReadOnlyDictionary<string, TValue> properties,
        string name)
        => properties.Keys.Any(key =>
            string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

    internal static bool TryGetIgnoreCase<TValue>(
        IReadOnlyDictionary<string, TValue> properties,
        string name,
        out TValue? value)
    {
        foreach (var (key, candidate) in properties)
        {
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                continue;

            value = candidate;
            return true;
        }

        value = default;
        return false;
    }

    internal static bool RemoveIgnoreCase<TValue>(
        IDictionary<string, TValue> properties,
        string name)
    {
        var key = properties.Keys.FirstOrDefault(key =>
            string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
        return key is not null && properties.Remove(key);
    }
}
