using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Mapping;
/// <summary>Maps one payload to one payload using an externally supplied function.</summary>
public sealed class Mapper : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    private readonly Func<FlowMessage, CancellationToken, ValueTask<FlowValue>> _map;
    public Mapper(MapperOptions options)
    {
        Validate(options);
        _map = options.MapAsync;
        var execution = options.Execution ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = options.OutputCapacity };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping, handlesErrors: options.HandleErrors), execution);
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => [message.WithValue(await _map(message, Stopping).ConfigureAwait(false))];
    private static void Validate(MapperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.MapAsync);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity, nameof(options.OutputCapacity));
    }
}
