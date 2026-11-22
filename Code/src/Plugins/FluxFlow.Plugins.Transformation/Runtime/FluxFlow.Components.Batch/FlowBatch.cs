using System.Text.Json;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Batch;

/// <summary>An immutable list snapshot preserving the original messages and their envelopes.</summary>
public sealed class FlowBatch : FlowValue
{
    private readonly FlowMessage[] _items;
    public IReadOnlyList<FlowMessage> Items { get; }

    public FlowBatch(IEnumerable<FlowMessage> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
        if (_items.Any(item => item is null)) throw new ArgumentException("Batch items cannot be null.", nameof(items));
        Items = Array.AsReadOnly(_items);
    }

    protected override JsonElement CreateJson() => JsonSerializer.SerializeToElement(_items);
}
