using System.Collections.ObjectModel;

namespace FluxFlow.Components.Projections.Contracts;

internal static class ProjectionContractNormalization
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal));

    internal static IReadOnlyDictionary<string, string> CopyAttributes(
        IReadOnlyDictionary<string, string>? source)
    {
        if (source is null || source.Count == 0)
            return EmptyAttributes;

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in source)
            copy.Add(key, value);

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
