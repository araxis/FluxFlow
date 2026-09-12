using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

internal static class MetadataEdit
{
    internal static ComponentMetadataText? Text(string? value) => value is null ? null : new(value);

    internal static IReadOnlyList<T> Replace<T>(IReadOnlyList<T> source, Func<T, bool> matches,
        Func<T, T> edit, string missingMessage)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (!matches(source[index])) continue;
            var result = source.ToArray();
            result[index] = edit(source[index]);
            return result;
        }
        throw new InvalidOperationException(missingMessage);
    }

    internal static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> Merge(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> source,
        params (string Name, string? Value)[] hints)
    {
        var result = new Dictionary<ComponentAttributeName, ComponentAttributeValue>(source);
        foreach (var (name, value) in hints)
            if (value is not null) result[new(name)] = new(value);
        return result;
    }
}

