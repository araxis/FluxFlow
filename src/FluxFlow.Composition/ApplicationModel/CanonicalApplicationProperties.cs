using System.Text.Json;

namespace FluxFlow.Composition.Model;

internal static class CanonicalApplicationProperties
{
    internal const string Resources = "Resources";
    internal const string Workflows = "Workflows";
    internal const string Type = "Type";
    internal const string Processing = "Processing";
    internal const string Name = "Name";
    internal const string LegacyConfiguration = "Configuration";
    internal const string LinkPort = "Port";
    internal const string LinkCondition = "Condition";
    internal const string DesignerProcessingOption = "processing";

    internal static IReadOnlyList<string> DesignerCompatibilityOptions { get; } =
    [
        "name",
        .. CompositionProcessingConfiguration.TechnicalOptionNames
    ];

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
