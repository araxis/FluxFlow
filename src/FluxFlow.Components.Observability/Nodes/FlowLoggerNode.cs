using System.Globalization;
using System.Text.Json;
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
public sealed class FlowLoggerNode : FlowLoggerNode<JsonElement>
{
    public FlowLoggerNode(
        FlowLoggerOptions options,
        IReadOnlyDictionary<string, IObservabilityValueSelector<JsonElement>>? attributeSelectors = null,
        TimeProvider? clock = null)
        : base(options, attributeSelectors, clock)
    {
    }
}

public class FlowLoggerNode<T> : IFlowNode
{
    private const string ComponentType = "log.write";

    private readonly FlowLoggerOptions _options;
    private readonly FlowLogLevel _level;
    private readonly IReadOnlyDictionary<string, IObservabilityValueSelector<T>> _selectors;
    private readonly ObservabilityNodeContext _nodeContext;
    private readonly TimeProvider _clock;
    private readonly ObservabilityPipeline<T, FlowLogEntry<T>> _pipeline;
    private readonly TemplateSegment[] _messageTemplate;
    private readonly string _inputTypeName;
    private readonly string _levelText;
    private long _sequence;

    public FlowLoggerNode(
        FlowLoggerOptions options,
        IReadOnlyDictionary<string, IObservabilityValueSelector<T>>? attributeSelectors = null,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options);
        _level = _options.ResolveLevel();
        _selectors = CopySelectors(attributeSelectors);
        _inputTypeName = typeof(T).FullName ?? typeof(T).Name;
        _levelText = _level.ToString();
        _messageTemplate = ParseTemplate(_options.EffectiveMessageTemplate, _selectors);
        _clock = clock ?? TimeProvider.System;
        _nodeContext = new ObservabilityNodeContext
        {
            NodeType = ComponentType,
            InputType = typeof(T),
            Name = _options.EffectiveCategory
        };
        _pipeline = new ObservabilityPipeline<T, FlowLogEntry<T>>(
            _options.BoundedCapacity,
            Process);
    }

    public ITargetBlock<FlowMessage<T>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowLogEntry<T>>> Output
        => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private FlowMessage<FlowLogEntry<T>> Process(FlowMessage<T> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message.WithError<FlowLogEntry<T>>(message.Error!);

        var timestamp = _clock.GetUtcNow();
        try
        {
            var sequence = ++_sequence;
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
            var selectorFailures = new List<SelectorFailure>();
            foreach (var (name, selector) in _selectors)
            {
                try
                {
                    attributes[name] = selector.Select(message.Value, _nodeContext);
                }
                catch (Exception exception)
                {
                    selectorFailures.Add(new SelectorFailure(name, exception));
                }
            }

            var entry = new FlowLogEntry<T>
            {
                Timestamp = timestamp,
                Level = _level,
                Category = _options.EffectiveCategory,
                Message = RenderMessage(message.Value, sequence, attributes),
                Sequence = sequence,
                Input = message.Value,
                Attributes = attributes
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
                return message.With(entry);
            }

            var names = selectorFailures.Select(failure => failure.Name).ToArray();
            var error = new DataFlowError(
                ObservabilityErrorCodeNames.LoggerAttributeSelectorFailed,
                $"flow.logger failed to select {names.Length} attribute(s): " +
                string.Join(", ", names),
                category: "Observability.Logger",
                isTransient: false,
                details: CreateSelectorErrorDetails(selectorFailures));
            PublishEvent(
                message,
                timestamp,
                ObservabilityDiagnosticNames.LoggerFailed,
                error.Message,
                ObservabilityResultKinds.LogEntryPartial,
                sequence,
                selectorFailures.Count,
                isError: true);
            return message.WithError<FlowLogEntry<T>>(error);
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

    private FlowMessage<FlowLogEntry<T>> Failure(
        FlowMessage<T> message,
        DateTimeOffset timestamp,
        string resultKind,
        string errorCode,
        string errorMessage,
        Exception? exception = null)
    {
        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["category"] = _options.EffectiveCategory
        };
        if (exception is not null)
        {
            details["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name;
        }

        var error = new DataFlowError(
            errorCode,
            errorMessage,
            category: "Observability.Logger",
            isTransient: false,
            details: JsonSerializer.SerializeToElement(details));
        PublishEvent(
            message,
            timestamp,
            ObservabilityDiagnosticNames.LoggerFailed,
            error.Message,
            resultKind,
            _sequence,
            failedSelectorCount: 0,
            isError: true);
        return message.WithError<FlowLogEntry<T>>(error);
    }

    private void PublishEvent(
        FlowMessage<T> message,
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
                ["inputType"] = typeof(T).FullName ?? typeof(T).Name,
                ["isError"] = isError,
                ["nodeType"] = ComponentType,
                ["resultKind"] = resultKind,
                ["sequence"] = sequence
            }
        });

    private string RenderMessage(
        T input,
        long sequence,
        IReadOnlyDictionary<string, object?> attributes)
    {
        var rendered = new System.Text.StringBuilder(_options.EffectiveMessageTemplate.Length);
        foreach (var segment in _messageTemplate)
        {
            object? value;
            var hasValue = true;
            switch (segment.Kind)
            {
                case TemplateSegmentKind.Literal:
                    rendered.Append(segment.Text);
                    continue;
                case TemplateSegmentKind.Category:
                    value = _options.EffectiveCategory;
                    break;
                case TemplateSegmentKind.Input:
                    value = input;
                    break;
                case TemplateSegmentKind.InputType:
                    value = _inputTypeName;
                    break;
                case TemplateSegmentKind.Level:
                    value = _levelText;
                    break;
                case TemplateSegmentKind.Sequence:
                    value = sequence;
                    break;
                case TemplateSegmentKind.Attribute:
                    hasValue = attributes.TryGetValue(segment.AttributeName!, out value);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Logger template segment kind '{segment.Kind}' is not supported.");
            }

            rendered.Append(hasValue ? FormatTemplateValue(value) : segment.Text);
        }

        return rendered.ToString();
    }

    private static TemplateSegment[] ParseTemplate(
        string template,
        IReadOnlyDictionary<string, IObservabilityValueSelector<T>> selectors)
    {
        var segments = new List<TemplateSegment>();
        var position = 0;
        while (position < template.Length)
        {
            var start = template.IndexOf('{', position);
            var end = start < 0 ? -1 : template.IndexOf('}', start + 1);
            if (end < 0)
            {
                AddLiteral(segments, template[position..]);
                break;
            }

            var key = template[(start + 1)..end];
            var kind = ResolveTemplateSegmentKind(key, selectors);
            if (kind.HasValue)
            {
                AddLiteral(segments, template[position..start]);
                segments.Add(new TemplateSegment(
                    template[start..(end + 1)],
                    kind.Value,
                    kind == TemplateSegmentKind.Attribute ? key : null));
                position = end + 1;
            }
            else
            {
                AddLiteral(segments, template[position..(start + 1)]);
                position = start + 1;
            }
        }

        return segments.ToArray();
    }

    private static TemplateSegmentKind? ResolveTemplateSegmentKind(
        string key,
        IReadOnlyDictionary<string, IObservabilityValueSelector<T>> selectors)
        => key switch
        {
            "category" => TemplateSegmentKind.Category,
            "input" => TemplateSegmentKind.Input,
            "inputType" => TemplateSegmentKind.InputType,
            "level" => TemplateSegmentKind.Level,
            "sequence" => TemplateSegmentKind.Sequence,
            _ when selectors.ContainsKey(key) => TemplateSegmentKind.Attribute,
            _ => null
        };

    private static void AddLiteral(List<TemplateSegment> segments, string value)
    {
        if (value.Length > 0)
            segments.Add(new TemplateSegment(value, TemplateSegmentKind.Literal));
    }

    private static string FormatTemplateValue(object? value)
        => value switch
        {
            null => string.Empty,
            JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString() ?? string.Empty,
            JsonElement json => json.GetRawText(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

    private static JsonElement CreateSelectorErrorDetails(
        IReadOnlyCollection<SelectorFailure> failures)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["exceptionTypes"] = failures.Select(failure =>
                failure.Exception.GetType().FullName ?? failure.Exception.GetType().Name).ToArray(),
            ["failedSelectors"] = failures.Select(failure => failure.Name).ToArray()
        });

    private static IReadOnlyDictionary<string, IObservabilityValueSelector<T>> CopySelectors(
        IReadOnlyDictionary<string, IObservabilityValueSelector<T>>? selectors)
    {
        var copy = new Dictionary<string, IObservabilityValueSelector<T>>(StringComparer.Ordinal);
        foreach (var (configuredName, selector) in selectors ??
            new Dictionary<string, IObservabilityValueSelector<T>>(StringComparer.Ordinal))
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

    private readonly record struct TemplateSegment(
        string Text,
        TemplateSegmentKind Kind,
        string? AttributeName = null);

    private enum TemplateSegmentKind
    {
        Literal,
        Category,
        Input,
        InputType,
        Level,
        Sequence,
        Attribute
    }
}
