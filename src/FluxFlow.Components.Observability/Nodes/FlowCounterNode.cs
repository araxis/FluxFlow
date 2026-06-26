using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Observability.Nodes;

/// <summary>
/// A standalone counter node. Post a <c>FlowMessage&lt;TInput&gt;</c> to <c>Input</c>;
/// the node counts accepted inputs and broadcasts a
/// <c>FlowMessage&lt;FlowCounterSnapshot&gt;</c> on <c>Output</c> carrying the same
/// correlation id. When an expression engine and predicate are configured, the
/// predicate is compiled once at construction and evaluated per message; inputs the
/// predicate rejects are not counted (and not emitted) but are tallied in the
/// snapshot's rejected count. With no predicate every input is counted and no engine
/// is required. Predicate-evaluation failures surface on <c>Errors</c> (with the
/// original correlation id) and the node keeps processing. Diagnostics flow on
/// <c>Events</c>.
/// </summary>
public sealed class FlowCounterNode<TInput> : FlowNode<TInput, FlowCounterSnapshot>
{
    public const string NodeType = "flow.counter";
    public const string Incremented = ObservabilityDiagnosticNames.CounterIncremented;
    public const string Rejected = ObservabilityDiagnosticNames.CounterRejected;
    public const string Failed = ObservabilityDiagnosticNames.CounterFailed;

    private readonly FlowCounterOptions _options;
    private readonly IFlowPredicate<TInput>? _acceptPredicate;
    private readonly string? _engineName;
    private readonly TimeProvider _clock;
    private long _count;
    private long _rejectedCount;

    public FlowCounterNode(
        FlowCounterOptions options,
        IFlowExpressionEngine? expressionEngine = null,
        IFlowMapContextFactory<TInput>? contextFactory = null,
        TimeProvider? clock = null)
        : this(ValidateOptions(options), expressionEngine, contextFactory, clock)
    {
    }

    private FlowCounterNode(
        ValidatedOptions options,
        IFlowExpressionEngine? expressionEngine,
        IFlowMapContextFactory<TInput>? contextFactory,
        TimeProvider? clock)
        : base(options.FlowNodeOptions)
    {
        _options = options.CounterOptions;
        _clock = clock ?? TimeProvider.System;
        _engineName = expressionEngine?.Name;

        // Compile the predicate expression once here (build time) when present;
        // when there is no predicate the node accepts every input and no engine is
        // required.
        var effectivePredicate = options.EffectivePredicate;
        if (!string.IsNullOrWhiteSpace(effectivePredicate))
        {
            if (expressionEngine is null)
            {
                throw new ArgumentNullException(
                    nameof(expressionEngine),
                    "flow.counter requires an expression engine when a predicate is configured.");
            }

            _acceptPredicate = contextFactory is null
                ? new ExpressionFlowPredicate<TInput>(effectivePredicate, expressionEngine)
                : new ExpressionFlowPredicate<TInput>(effectivePredicate, expressionEngine, contextFactory);
        }
    }

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;
        if (!IsAccepted(input, message))
        {
            return Task.CompletedTask;
        }

        var observedAt = _clock.GetUtcNow();
        var count = Interlocked.Increment(ref _count);
        var snapshot = new FlowCounterSnapshot
        {
            Timestamp = observedAt,
            Name = _options.EffectiveName,
            InputType = _options.InputType,
            Count = count,
            RejectedCount = Volatile.Read(ref _rejectedCount),
            LastObservedAt = observedAt
        };

        // Carry the correlation id forward onto the snapshot.
        Emit(message.With(snapshot));
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = Incremented,
            Level = FlowEventLevel.Information,
            Message = "flow.counter incremented.",
            Attributes = ObservabilityNodeSupport.CreateAttributes(
                NodeType,
                _options.InputType,
                _options.EffectiveName,
                count)
        });

        return Task.CompletedTask;
    }

    private bool IsAccepted(TInput input, FlowMessage<TInput> message)
    {
        if (_acceptPredicate is null)
        {
            return true;
        }

        try
        {
            var accepted = _acceptPredicate.IsMatch(input);

            if (!accepted)
            {
                var rejected = Interlocked.Increment(ref _rejectedCount);
                EmitEvent(new FlowEvent
                {
                    Timestamp = _clock.GetUtcNow(),
                    CorrelationId = message.CorrelationId,
                    Name = Rejected,
                    Level = FlowEventLevel.Information,
                    Message = "flow.counter rejected input.",
                    Attributes = ObservabilityNodeSupport.CreateAttributes(
                        NodeType,
                        _options.InputType,
                        _options.EffectiveName,
                        rejected)
                });
            }

            return accepted;
        }
        catch (Exception exception)
        {
            EmitError(new FlowError
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Code = ObservabilityErrorCodes.CounterPredicateFailed,
                Message = $"flow.counter failed to evaluate input: {exception.Message}",
                Context = ObservabilityNodeSupport.CreateExpressionContext(_options, _engineName),
                Exception = exception
            });
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = Failed,
                Level = FlowEventLevel.Error,
                Message = "flow.counter failed to evaluate input.",
                Attributes = ObservabilityNodeSupport.CreateAttributes(
                    NodeType,
                    _options.InputType,
                    _options.EffectiveName)
            });
            return false;
        }
    }

    private static ValidatedOptions ValidateOptions(FlowCounterOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new ArgumentException(
                "flow.counter option 'inputType' cannot be empty.", nameof(options));
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.counter option 'boundedCapacity' must be greater than zero.");
        }

        return new ValidatedOptions(options);
    }

    private sealed class ValidatedOptions(FlowCounterOptions counterOptions)
    {
        public FlowCounterOptions CounterOptions { get; } = counterOptions;

        public string? EffectivePredicate { get; } = counterOptions.EffectivePredicate;

        public FlowNodeOptions FlowNodeOptions { get; } = new()
        {
            InputCapacity = counterOptions.BoundedCapacity
        };
    }
}
