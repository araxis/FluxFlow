using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>
/// A standalone fork node. Post a <c>FlowMessage&lt;TInput&gt;</c> to <c>Input</c>; the
/// node copies it to every configured output port, each carrying the same correlation id.
/// The first configured output is the primary <c>Output</c>; the remaining outputs are
/// extra broadcast ports surfaced by name via <see cref="Outputs"/>. Works with nothing
/// but <c>new FlowForkNode&lt;T&gt;(options)</c> — no engine.
/// </summary>
public sealed class FlowForkNode<TInput> : FlowNode<TInput, TInput>
{
    private readonly ForkRoutingOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, BroadcastBlock<FlowMessage<TInput>>?> _outputBlocks;
    private readonly IReadOnlyDictionary<string, ISourceBlock<FlowMessage<TInput>>> _outputs;

    public FlowForkNode(
        ForkRoutingOptions options,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (options ?? throw new ArgumentNullException(nameof(options))).BoundedCapacity
        })
    {
        _options = options;
        _clock = clock ?? TimeProvider.System;
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.fork bounded capacity must be greater than zero.");
        }

        RoutingPortNames.Validate(
            "flow.fork",
            "outputs",
            options.Outputs,
            [RoutingComponentPorts.Input, RoutingComponentPorts.Errors]);

        // The first output reuses the primary Output port; the rest are extra broadcast
        // ports created with AddOutput. A null value marks "this name maps to Output".
        var ordered = options.Outputs.Select(output => output.Trim()).ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        _outputBlocks = new Dictionary<string, BroadcastBlock<FlowMessage<TInput>>?>(StringComparer.Ordinal);
        var outputs = new Dictionary<string, ISourceBlock<FlowMessage<TInput>>>(StringComparer.Ordinal);
        for (var index = 0; index < ordered.Length; index++)
        {
            var name = ordered[index];
            if (!seen.Add(name))
            {
                continue;
            }

            if (index == 0)
            {
                _outputBlocks[name] = null;
                outputs[name] = Output;
            }
            else
            {
                var port = AddOutput<FlowMessage<TInput>>();
                _outputBlocks[name] = port;
                outputs[name] = port;
            }
        }

        _outputs = outputs;
    }

    /// <summary>Configured output ports keyed by name. The first is the primary <c>Output</c>.</summary>
    public IReadOnlyDictionary<string, ISourceBlock<FlowMessage<TInput>>> Outputs => _outputs;

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        foreach (var output in _outputBlocks.Values)
        {
            if (output is null)
            {
                Emit(message);
            }
            else
            {
                output.Post(message);
            }
        }

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = RoutingDiagnosticNames.ForkForwarded,
            Level = FlowEventLevel.Information,
            Message = "flow.fork forwarded value.",
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["inputType"] = _options.InputType,
                ["outputs"] = _outputBlocks.Count
            }
        });

        return Task.CompletedTask;
    }
}
