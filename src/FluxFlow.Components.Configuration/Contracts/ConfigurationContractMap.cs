namespace FluxFlow.Components.Configuration.Contracts;

internal static class ConfigurationContractMap
{
    public static IReadOnlyDictionary<string, string>? NormalizeOrPreserveInvalid(
        IReadOnlyDictionary<string, string>? values)
    {
        if (values is null)
            return null;

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.Key) || string.IsNullOrWhiteSpace(value.Value))
                return values;

            if (!normalized.TryAdd(value.Key.Trim(), value.Value.Trim()))
                return values;
        }

        return normalized;
    }

    public static IReadOnlyList<string> FindDuplicateNormalizedKeys(
        IReadOnlyDictionary<string, string>? values)
    {
        if (values is null)
            return [];

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value.Key))
            .GroupBy(value => value.Key.Trim(), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
    }
}
