namespace FluxFlow.Composition;

internal static class CompositionDictionary
{
    public static Dictionary<string, TValue> NormalizeKeys<TValue>(
        IReadOnlyDictionary<string, TValue>? source,
        string collectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        var result = new Dictionary<string, TValue>(StringComparer.Ordinal);
        if (source is null)
            return result;

        foreach (var (key, value) in source)
        {
            var normalizedKey = key?.Trim() ?? string.Empty;
            if (!result.TryAdd(normalizedKey, value))
            {
                throw new ArgumentException(
                    $"{collectionName} contains duplicate key '{normalizedKey}' after trimming.",
                    collectionName);
            }
        }

        return result;
    }
}
