namespace FluxFlow.Components.Metrics.Contracts;

internal static class MetricsContractNormalization
{
    public static string NormalizeRequired(string value)
        => value?.Trim() ?? string.Empty;

    public static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    public static Dictionary<string, string> CopyTags(
        Dictionary<string, string>? source)
        => source is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

    public static Dictionary<string, MetricGroupSnapshot> CopyGroups(
        Dictionary<string, MetricGroupSnapshot>? source)
    {
        var target = new Dictionary<string, MetricGroupSnapshot>(StringComparer.Ordinal);
        if (source is null)
        {
            return target;
        }

        foreach (var (key, value) in source)
        {
            if (value is not null)
            {
                target[key] = value with { };
            }
        }

        return target;
    }

    public static MetricSampleInput? CopySample(MetricSampleInput? source)
        => source is null
            ? null
            : source with { Tags = CopyTags(source.Tags) };
}
