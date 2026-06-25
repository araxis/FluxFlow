namespace FluxFlow.Components.Journal.Contracts;

internal static class JournalContractNormalization
{
    public static string NormalizeRequired(string? value)
        => value?.Trim() ?? string.Empty;

    public static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    public static IReadOnlyDictionary<string, string> NormalizeAttributes(
        IReadOnlyDictionary<string, string>? attributes,
        string owner)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (attributes is null)
            return normalized;

        foreach (var (key, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException($"{owner} attribute keys are required.");

            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{owner} attribute values are required.");

            var normalizedKey = key.Trim();
            if (!normalized.TryAdd(normalizedKey, value.Trim()))
                throw new ArgumentException($"{owner} attribute '{normalizedKey}' is declared more than once.");
        }

        return normalized;
    }

    public static IReadOnlyList<JournalRecord> CopyRecords(
        IEnumerable<JournalRecord>? records)
        => records?.ToArray() ?? [];
}
