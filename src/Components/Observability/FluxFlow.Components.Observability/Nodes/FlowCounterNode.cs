using System.Text.Json;
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
public sealed class FlowCounterNode(
    FlowCounterOptions options,
    IFlowExpressionEngine? expressionEngine = null,
    IFlowMapContextFactory<JsonElement>? contextFactory = null,
    TimeProvider? clock = null)
    : FlowCounterNode<JsonElement>(options, expressionEngine, contextFactory, clock);

public class FlowCounterNode<T> : FlowNode
{
    private const string ComponentType = "metric.count";

    private readonly FlowCounterOptions _options;
    private readonly IFlowPredicate<T>? _predicate;
    private readonly string? _engineName;
    private readonly TimeProvider _clock;
    private long _count;
    private long _rejectedCount;

    public FlowCounterNode(
        FlowCounterOptions options,
        IFlowExpressionEngine? expressionEngine = null,
        IFlowMapContextFactory<T>? contextFactory = null,
        TimeProvider? clock = null)
        : base(CreateNodeOptions(options))
    {
        _options = ValidateOptions(options);
        _clock = clock ?? TimeProvider.System;
        _engineName = expressionEngine?.Name;

        if (!string.IsNullOrWhiteSpace(_options.Predicate))
        {
            if (expressionEngine is null)
            {
                throw new ArgumentNullException(
                    nameof(expressionEngine),
                    "flow.counter requires an expression engine when a predicate is configured.");
            }

            _predicate = contextFactory is null
                ? new ExpressionFlowPredicate<T>(
                    _options.Predicate,
                    expressionEngine)
                : new ExpressionFlowPredicate<T>(
                    _options.Predicate,
                    expressionEngine,
                    contextFactory);
        }
    }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage message)
        => await EmitAsync(Process(message), Stopping).ConfigureAwait(false);

    private FlowMessage Process(FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message;

        var timestamp = _clock.GetUtcNow();
        bool accepted;
        try
        {
            var input = (message.Value is T typedInput0 ? typedInput0 : typeof(T) == typeof(JsonElement) ? (T)(object)message.Value.Json : message.Value.Json.Deserialize<T>(ValueJsonOptions))!;
            accepted = _predicate?.IsMatch(input) ?? true;
        }
        catch (Exception exception)
        {
            FlowDiagnostics.ProcessingException(message, this, exception);
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
            InputType = typeof(T).FullName ?? typeof(T).Name,
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
        return message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(snapshot, ValueJsonOptions)));
    }

    private FlowMessage Failure(
        FlowMessage message,
        DateTimeOffset timestamp,
        string errorCode,
        string errorMessage,
        Exception? exception = null)
    {
        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["engine"] = _engineName,
            ["expression"] = _options.Predicate,
            ["name"] = _options.EffectiveName
        };
        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
            details["expressionId"] = _options.ExpressionId;
        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
            details["expressionName"] = _options.ExpressionName;
        if (exception is not null)
        {
            details["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name;
        }

        var error = new DataFlowError(
            errorCode,
            errorMessage,
            category: "Observability.Counter",
            isTransient: false,
            details: JsonSerializer.SerializeToElement(details));
        PublishEvent(
            message,
            timestamp,
            ObservabilityDiagnosticNames.CounterFailed,
            error.Message,
            ObservabilityResultKinds.CounterFailed,
            _count,
            _rejectedCount,
            isError: true);
        return message.WithError(error);
    }

    private void PublishEvent(
        FlowMessage message,
        DateTimeOffset timestamp,
        string name,
        string text,
        string resultKind,
        long count,
        long rejectedCount,
        bool isError)
        => EmitEvent(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = isError ? FlowEventLevel.Warning : FlowEventLevel.Information,
            Message = text,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["count"] = count,
                ["inputType"] = typeof(T).FullName ?? typeof(T).Name,
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

    private static FlowNodeOptions CreateNodeOptions(FlowCounterOptions? options)
    {
        var validated = ValidateOptions(options);
        return new FlowNodeOptions
        {
            InputCapacity = validated.BoundedCapacity,
            OutputCapacity = validated.BoundedCapacity
        };
    }
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);
}
