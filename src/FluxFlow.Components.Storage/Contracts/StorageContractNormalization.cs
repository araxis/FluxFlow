namespace FluxFlow.Components.Storage.Contracts;

internal static class StorageContractNormalization
{
    public static string NormalizeRequired(string? value)
        => NormalizeOptional(value) ?? string.Empty;

    public static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public static Dictionary<string, string> CopyAttributes(
        Dictionary<string, string>? source)
        => source is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

    public static StorageRecord? CopyRecord(StorageRecord? record)
        => record is null
            ? null
            : record with
            {
                Attributes = CopyAttributes(record.Attributes)
            };

    public static IReadOnlyList<StorageRecord> CopyRecords(
        IEnumerable<StorageRecord>? source)
        => source is null
            ? []
            : source
                .Select(record => CopyRecord(record)!)
                .ToArray();
}
