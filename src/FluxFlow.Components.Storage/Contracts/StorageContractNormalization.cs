using System.Collections.ObjectModel;

namespace FluxFlow.Components.Storage.Contracts;

internal static class StorageContractNormalization
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal));

    public static string NormalizeRequired(string? value)
        => NormalizeOptional(value) ?? string.Empty;

    public static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public static IReadOnlyDictionary<string, string> CopyAttributes(
        IReadOnlyDictionary<string, string>? source)
    {
        if (source is null || source.Count == 0)
            return EmptyAttributes;

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Storage attribute keys are required.");
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Storage attribute values are required.");

            var normalizedKey = key.Trim();
            if (!copy.TryAdd(normalizedKey, value.Trim()))
            {
                throw new InvalidOperationException(
                    $"Storage attribute '{normalizedKey}' is declared more than once.");
            }
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }

}
