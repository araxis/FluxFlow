using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>
/// A standalone merge (fan-in) node. Several upstreams of the same type all link into the
/// single bounded <c>Input</c> — a <see cref="System.Threading.Tasks.Dataflow.BufferBlock{T}"/>
/// already merges concurrent producers — and the node re-broadcasts each message on
/// <c>Output</c> in arrival order, carrying the correlation id forward and stamping a
/// monotonic sequence number into the merge diagnostic. Output completes once every linked
/// upstream completes the input. Works with nothing but <c>new FlowMergeNode&lt;T&gt;(options)</c>
/// — no engine.
/// </summary>
public sealed class FlowMergeNode<TInput> : FlowNode<TInput, TInput>
{
    private readonly MergeRoutingOptions _options;
    private readonly TimeProvider _clock;
    private long _nextSequence;

    public FlowMergeNode(
        MergeRoutingOptions options,
        TimeProvider? clock = null)
        : this(ValidateOptions(options), clock)
    {
    }

    private FlowMergeNode(ValidatedOptions options, TimeProvider? clock)
        : base(options.FlowNodeOptions)
    {
        _options = options.MergeOptions;
        _clock = clock ?? TimeProvider.System;
    }

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sequence = Interlocked.Increment(ref _nextSequence);

        // Re-broadcast the same envelope; the correlation id flows forward unchanged.
        Emit(message);
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = RoutingDiagnosticNames.MergeEmitted,
            Level = FlowEventLevel.Information,
            Message = "flow.merge emitted value.",
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["inputType"] = _options.InputType,
                ["sequence"] = sequence
            }
        });

        return Task.CompletedTask;
    }

    private static ValidatedOptions ValidateOptions(MergeRoutingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new ArgumentException(
                "flow.merge option 'inputType' cannot be empty.", nameof(options));
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.merge option 'boundedCapacity' must be greater than zero.");
        }

        return new ValidatedOptions(options);
    }

    private sealed class ValidatedOptions(MergeRoutingOptions mergeOptions)
    {
        public MergeRoutingOptions MergeOptions { get; } = mergeOptions;

        public FlowNodeOptions FlowNodeOptions { get; } = new()
        {
            InputCapacity = mergeOptions.BoundedCapacity
        };
    }
}
