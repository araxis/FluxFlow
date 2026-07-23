using System.Globalization;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Observability.Nodes;

/// <summary>
/// Renders structured log data from immutable workflow values and emits
/// complete, partial, and expected failure outcomes through one normal output.
/// </summary>
public sealed class FlowLoggerNode : IFlowNode
{
    private const string ComponentType = "log.write";
    private const string InputType = nameof(FlowValue);

    private readonly FlowLoggerOptions _options;
    private readonly FlowLogLevel _level;
    private readonly IReadOnlyDictionary<string, IObservabilityValueSelector> _selectors;
    private readonly ObservabilityNodeContext _nodeContext;
    private readonly TimeProvider _clock;
    private readonly ObservabilityPipeline<FlowLogEntry> _pipeline;
    private long _sequence;

    public FlowLoggerNode(
        FlowLoggerOptions options,
        IReadOnlyDictionary<string, IObservabilityValueSelector>? attributeSelectors = null,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options);
        _level = _options.ResolveLevel();
        _selectors = CopySelectors(attributeSelectors);
        _clock = clock ?? TimeProvider.System;
        _nodeContext = new ObservabilityNodeContext
        {
            NodeType = ComponentType,
            InputType = typeof(FlowValue),
            Name = _options.EffectiveCategory
        };
        _pipeline = new ObservabilityPipeline<FlowLogEntry>(
            _options.BoundedCapacity,
            Process);
    }

    public ITargetBlock<FlowMessage<FlowValue>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<FlowLogEntry>>> Output
        => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private FlowMessage<FlowResult<FlowLogEntry>> Process(
        FlowMessage<FlowValue> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var timestamp = _clock.GetUtcNow();
        if (message.Payload is null)
        {
            return Failure(
                message,
                timestamp,
                ObservabilityResultKinds.LoggerFailed,
                ObservabilityErrorCodeNames.MissingInput,
                "flow.logger requires FlowValue input.");
        }

        try
        {
            var sequence = ++_sequence;
            var attributes = new Dictionary<string, FlowValue>(StringComparer.Ordinal);
            var selectorFailures = new List<SelectorFailure>();
            foreach (var (name, selector) in _selectors)
            {
                try
                {
                    attributes[name] = selector.Select(message.Payload, _nodeContext)
                        ?? FlowValue.Null;
                }
                catch (Exception exception)
                {
                    selectorFailures.Add(new SelectorFailure(name, exception));
                }
            }

            var entry = new FlowLogEntry
            {
                Timestamp = timestamp,
                Level = _level,
                Category = _options.EffectiveCategory,
                Message = RenderMessage(
                    _options.EffectiveMessageTemplate,
                    message.Payload,
                    sequence,
                    attributes),
                Sequence = sequence,
                Input = message.Payload,
                Attributes = FlowValue.FromObject(attributes)
            };
            if (selectorFailures.Count == 0)
            {
                PublishEvent(
                    message,
                    timestamp,
                    ObservabilityDiagnosticNames.LoggerEmitted,
                    "flow.logger emitted entry.",
                    ObservabilityResultKinds.LogEntry,
                    sequence,
                    failedSelectorCount: 0,
                    isError: false);
                return message.With(FlowResult<FlowLogEntry>.Success(
                    ObservabilityResultKinds.LogEntry,
                    entry,
                    timestamp));
            }

            var names = selectorFailures.Select(failure => failure.Name).ToArray();
            var error = new DataFlowError(
                ObservabilityErrorCodeNames.LoggerAttributeSelectorFailed,
                $"flow.logger failed to select {names.Length} attribute(s): " +
                string.Join(", ", names),
                category: "Observability.Logger",
                isTransient: false,
                details: CreateSelectorErrorDetails(message.Payload, selectorFailures));
            PublishEvent(
                message,
                timestamp,
                ObservabilityDiagnosticNames.LoggerFailed,
                error.Message,
                ObservabilityResultKinds.LogEntryPartial,
                sequence,
                selectorFailures.Count,
                isError: true);
            return message.With(FlowResult<FlowLogEntry>.Failure(
                ObservabilityResultKinds.LogEntryPartial,
                error,
                timestamp,
                entry));
        }
        catch (Exception exception)
        {
            return Failure(
                message,
                timestamp,
                ObservabilityResultKinds.LoggerFailed,
                ObservabilityErrorCodeNames.LoggerFailed,
                $"flow.logger failed to render input: {exception.Message}",
                exception);
        }
    }

    private FlowMessage<FlowResult<FlowLogEntry>> Failure(
        FlowMessage<FlowValue> message,
        DateTimeOffset timestamp,
        string resultKind,
        string errorCode,
        string errorMessage,
        Exception? exception = null)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["category"] = FlowValue.From(_options.EffectiveCategory),
            ["input"] = message.Payload ?? FlowValue.Null
        };
        if (exception is not null)
        {
            details["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name);
        }

        var error = new DataFlowError(
            errorCode,
            errorMessage,
            category: "Observability.Logger",
            isTransient: false,
            details: FlowValue.FromObject(details));
        PublishEvent(
            message,
            timestamp,
            ObservabilityDiagnosticNames.LoggerFailed,
            error.Message,
            resultKind,
            _sequence,
            failedSelectorCount: 0,
            isError: true);
        return message.With(FlowResult<FlowLogEntry>.Failure(
            resultKind,
            error,
            timestamp));
    }

    private void PublishEvent(
        FlowMessage<FlowValue> message,
        DateTimeOffset timestamp,
        string name,
        string text,
        string resultKind,
        long sequence,
        int failedSelectorCount,
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
                ["category"] = _options.EffectiveCategory,
                ["failedSelectorCount"] = failedSelectorCount,
                ["inputType"] = InputType,
                ["isError"] = isError,
                ["nodeType"] = ComponentType,
                ["resultKind"] = resultKind,
                ["sequence"] = sequence
            }
        });

    private string RenderMessage(
        string template,
        FlowValue input,
        long sequence,
        IReadOnlyDictionary<string, FlowValue> attributes)
    {
        var values = new Dictionary<string, FlowValue>(attributes, StringComparer.Ordinal)
        {
            ["category"] = FlowValue.From(_options.EffectiveCategory),
            ["input"] = input,
            ["inputType"] = FlowValue.From(InputType),
            ["level"] = FlowValue.From(_level.ToString()),
            ["sequence"] = FlowValue.From(sequence)
        };
        var rendered = new System.Text.StringBuilder(template.Length);
        var position = 0;
        while (position < template.Length)
        {
            var start = template.IndexOf('{', position);
            var end = start < 0 ? -1 : template.IndexOf('}', start + 1);
            if (end < 0)
            {
                rendered.Append(template, position, template.Length - position);
                break;
            }

            var key = template.Substring(start + 1, end - start - 1);
            if (values.TryGetValue(key, out var value))
            {
                rendered.Append(template, position, start - position);
                rendered.Append(FormatTemplateValue(value));
                position = end + 1;
            }
            else
            {
                rendered.Append(template, position, start + 1 - position);
                position = start + 1;
            }
        }

        return rendered.ToString();
    }

    private static string FormatTemplateValue(FlowValue value)
        => value.Kind switch
        {
            FlowValueKind.Null => string.Empty,
            FlowValueKind.Boolean => value.GetBoolean().ToString(CultureInfo.InvariantCulture),
            FlowValueKind.Integer => value.GetInteger().ToString(CultureInfo.InvariantCulture),
            FlowValueKind.Decimal => value.GetDecimal().ToString(CultureInfo.InvariantCulture),
            FlowValueKind.FloatingPoint => value.GetFloatingPoint().ToString(
                CultureInfo.InvariantCulture),
            FlowValueKind.String => value.GetString(),
            _ => value.ToString()
        };

    private static FlowValue CreateSelectorErrorDetails(
        FlowValue input,
        IReadOnlyCollection<SelectorFailure> failures)
        => FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["exceptionTypes"] = FlowValue.FromArray(failures.Select(failure =>
                FlowValue.From(
                    failure.Exception.GetType().FullName ??
                    failure.Exception.GetType().Name))),
            ["failedSelectors"] = FlowValue.FromArray(failures.Select(failure =>
                FlowValue.From(failure.Name))),
            ["input"] = input
        });

    private static IReadOnlyDictionary<string, IObservabilityValueSelector> CopySelectors(
        IReadOnlyDictionary<string, IObservabilityValueSelector>? selectors)
    {
        var copy = new Dictionary<string, IObservabilityValueSelector>(StringComparer.Ordinal);
        foreach (var (configuredName, selector) in selectors ??
            new Dictionary<string, IObservabilityValueSelector>(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(configuredName))
                throw new ArgumentException("flow.logger selector names must be non-empty.", nameof(selectors));
            ArgumentNullException.ThrowIfNull(selector);
            var name = configuredName.Trim();
            if (!copy.TryAdd(name, selector))
            {
                throw new ArgumentException(
                    $"flow.logger selector '{name}' is configured more than once.",
                    nameof(selectors));
            }
        }

        return copy;
    }

    private static FlowLoggerOptions ValidateOptions(
        FlowLoggerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.logger option 'boundedCapacity' must be greater than zero.");
        }

        return options;
    }

    private sealed record SelectorFailure(string Name, Exception Exception);
}
