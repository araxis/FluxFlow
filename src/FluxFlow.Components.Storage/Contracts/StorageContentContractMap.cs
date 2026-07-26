using System.Collections.ObjectModel;

namespace FluxFlow.Components.Storage.Contracts;

internal static class StorageContentContractMap
{
    public static IReadOnlyDictionary<string, string> CopyAttributes(
        IReadOnlyDictionary<string, string>? source)
    {
        var copy = source is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(source, StringComparer.Ordinal);
        return new ReadOnlyDictionary<string, string>(copy);
    }

    public static IReadOnlyList<StorageContentRecord> CopyRecords(
        IReadOnlyList<StorageContentRecord>? source)
        => source is null || source.Count == 0
            ? Array.Empty<StorageContentRecord>()
            : Array.AsReadOnly(source.Select(CopyRecord).ToArray());

    public static StorageContentRecord CopyRecord(StorageContentRecord source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source with
        {
            Content = CopyContent(source.Content),
            Attributes = CopyAttributes(source.Attributes)
        };
    }

    public static FluxFlow.Data.FlowContent CopyContent(FluxFlow.Data.FlowContent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return FluxFlow.Data.FlowContent.FromBytes(
            source.Bytes.AsSpan().ToArray(),
            source.ContentType,
            source.Encoding);
    }

    public static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
