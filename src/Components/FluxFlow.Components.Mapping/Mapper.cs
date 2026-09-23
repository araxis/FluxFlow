using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Mapping;

/// <summary>Maps one payload to one payload using an externally supplied function.</summary>
public sealed class Mapper : FlowTransformNode
{
    private readonly Func<FlowMessage, CancellationToken, ValueTask<FlowValue>> _map;

    public Mapper(MapperOptions options) : base(Validate(options).Execution, options.OutputCapacity)
        => _map = options.MapAsync;

    protected override async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        => [message.WithValue(await _map(message, Stopping).ConfigureAwait(false))];

    private static MapperOptions Validate(MapperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.MapAsync);
        return options;
    }
}
