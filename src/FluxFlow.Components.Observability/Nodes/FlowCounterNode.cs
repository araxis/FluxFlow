using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Observability.Nodes;

/// <summary>
/// Counts immutable workflow values and emits counted, rejected, and expected
/// evaluation outcomes through one normal result output.
/// </summary>
public sealed class FlowCounterNode : IFlowNode
{
    private const string ComponentType = "metric.count";
    private const string InputType = nameof(FlowValue);

    private readonly FlowCounterOptions _options;
    private readonly IFlowPredicate<FlowValue>? _predicate;
    private readonly string? _engineName;
    private readonly TimeProvider _clock;
    private readonly ObservabilityPipeline<FlowCounterSnapshot> _pipeline;
    private long _count;
    private long _rejectedCount;

    public FlowCounterNode(
        FlowCounterOptions options,
        IFlowExpressionEngine? expressionEngine = null,
        IFlowMapContextFactory<FlowValue>? contextFactory = null,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options);
        _clock = clock ?? TimeProvider.System;
        _engineName = expressionEngine?.Name;

        if (!string.IsNullOrWhiteSpace(_options.EffectivePredicate))
        {
            if (expressionEngine is null)
            {
                throw new ArgumentNullException(
                    nameof(expressionEngine),
                    "flow.counter requires an expression engine when a predicate is configured.");
            }

            _predicate = contextFactory is null
                ? new ExpressionFlowPredicate<FlowValue>(
                    _options.EffectivePredicate,
                    expressionEngine)
                : new ExpressionFlowPredicate<FlowValue>(
                    _options.EffectivePredicate,
                    expressionEngine,
                    contextFactory);
        }

        _pipeline = new ObservabilityPipeline<FlowCounterSnapshot>(
            _options.BoundedCapacity,
            Process);
    }

    public ITargetBlock<FlowMessage<FlowValue>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<FlowCounterSnapshot>>> Output
        => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private FlowMessage<FlowResult<FlowCounterSnapshot>> Process(
        FlowMessage<FlowValue> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var timestamp = _clock.GetUtcNow();
        if (message.Payload is null)
        {
            return Failure(
                message,
                timestamp,
                ObservabilityErrorCodeNames.MissingInput,
                "flow.counter requires FlowValue input.");
        }

        bool accepted;
        try
        {
            accepted = _predicate?.IsMatch(message.Payload) ?? true;
        }
        catch (Exception exception)
        {
            return Failure(
                message,
                timestamp,
                ObservabilityErrorCodeNames.CounterPredicateFailed,
                $"flow.counter failed to evaluate input: {exception.Message}",
                exception);
        }

        var kind = accepted
            ? ObservabilityResultKinds.CounterSnapshot
            : ObservabilityResultKinds.CounterRejected;
        var count = accepted ? ++_count : _count;
        var rejectedCount = accepted ? _rejectedCount : ++_rejectedCount;
        var snapshot = new FlowCounterSnapshot
        {
            Timestamp = timestamp,
            Name = _options.EffectiveName,
            InputType = InputType,
            Count = count,
            RejectedCount = rejectedCount,
            LastObservedAt = timestamp
        };
        PublishEvent(
            message,
            timestamp,
            accepted
                ? ObservabilityDiagnosticNames.CounterIncremented
                : ObservabilityDiagnosticNames.CounterRejected,
            accepted ? "flow.counter incremented." : "flow.counter rejected input.",
            kind,
            count,
            rejectedCount,
            isError: false);
        return message.With(FlowResult<FlowCounterSnapshot>.Success(
            kind,
            snapshot,
            timestamp));
    }

    private FlowMessage<FlowResult<FlowCounterSnapshot>> Failure(
        FlowMessage<FlowValue> message,
        DateTimeOffset timestamp,
        string errorCode,
        string errorMessage,
        Exception? exception = null)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["engine"] = FlowValue.From(_engineName ?? string.Empty),
            ["expression"] = FlowValue.From(_options.EffectivePredicate ?? string.Empty),
            ["input"] = message.Payload ?? FlowValue.Null,
            ["name"] = FlowValue.From(_options.EffectiveName)
        };
        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
            details["expressionId"] = FlowValue.From(_options.ExpressionId);
        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
            details["expressionName"] = FlowValue.From(_options.ExpressionName);
        if (exception is not null)
        {
            details["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name);
        }

        var error = new DataFlowError(
            errorCode,
            errorMessage,
            category: "Observability.Counter",
            isTransient: false,
            details: FlowValue.FromObject(details));
        PublishEvent(
            message,
            timestamp,
            ObservabilityDiagnosticNames.CounterFailed,
            error.Message,
            ObservabilityResultKinds.CounterFailed,
            _count,
            _rejectedCount,
            isError: true);
        return message.With(FlowResult<FlowCounterSnapshot>.Failure(
            ObservabilityResultKinds.CounterFailed,
            error,
            timestamp));
    }

    private void PublishEvent(
        FlowMessage<FlowValue> message,
        DateTimeOffset timestamp,
        string name,
        string text,
        string resultKind,
        long count,
        long rejectedCount,
        bool isError)
        => _pipeline.PublishEvent(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = isError ? FlowEventLevel.Warning : FlowEventLevel.Information,
            Message = text,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["count"] = count,
                ["inputType"] = InputType,
                ["isError"] = isError,
                ["name"] = _options.EffectiveName,
                ["nodeType"] = ComponentType,
                ["rejectedCount"] = rejectedCount,
                ["resultKind"] = resultKind
            }
        });

    private static FlowCounterOptions ValidateOptions(
        FlowCounterOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.counter option 'boundedCapacity' must be greater than zero.");
        }

        return options;
    }
}
