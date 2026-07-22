using System.Collections.Immutable;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed record CompositionComponentEvent
{
    private IReadOnlyDictionary<string, FlowValue> _attributes =
        ImmutableDictionary.Create<string, FlowValue>(StringComparer.Ordinal);

    public required string ComponentAddress { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string Name { get; init; }

    public FlowEventLevel Level { get; init; } = FlowEventLevel.Information;

    public string? Message { get; init; }

    public IReadOnlyDictionary<string, FlowValue> Attributes
    {
        get => _attributes;
        init => _attributes = CopyAttributes(value);
    }

    private static IReadOnlyDictionary<string, FlowValue> CopyAttributes(
        IReadOnlyDictionary<string, FlowValue>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
            return ImmutableDictionary.Create<string, FlowValue>(StringComparer.Ordinal);

        var builder = ImmutableDictionary.CreateBuilder<string, FlowValue>(StringComparer.Ordinal);
        foreach (var (name, value) in attributes)
        {
            builder.Add(
                name,
                value ?? throw new ArgumentException(
                    "Component event attributes cannot contain null values; use FlowValue.Null.",
                    nameof(attributes)));
        }

        return builder.ToImmutable();
    }
}
