using System.Collections.Immutable;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed record CompositionComponentEvent
{
    private IReadOnlyDictionary<string, string> _attributes =
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

    public required string ComponentAddress { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string Name { get; init; }

    public FlowEventLevel Level { get; init; } = FlowEventLevel.Information;

    public string? Message { get; init; }

    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = CopyAttributes(value);
    }

    private static IReadOnlyDictionary<string, string> CopyAttributes(
        IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
            return ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in attributes)
        {
            builder.Add(
                name,
                value ?? throw new ArgumentException(
                    "Component event attributes cannot contain null values.",
                    nameof(attributes)));
        }

        return builder.ToImmutable();
    }
}
