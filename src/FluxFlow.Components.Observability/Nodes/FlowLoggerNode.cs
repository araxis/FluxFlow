using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Observability.Nodes;

/// <summary>
/// A standalone structured-logging node. Post a <c>FlowMessage&lt;TInput&gt;</c> to
/// <c>Input</c>; the node renders a message template, selects configured attributes
/// from the payload, and broadcasts a <c>FlowMessage&lt;FlowLogEntry&gt;</c> on
/// <c>Output</c> carrying the same correlation id. Attribute-selector failures
/// surface on <c>Errors</c> (with the original correlation id), the offending
/// attribute is skipped, and the node still emits the entry. Diagnostics flow on
/// <c>Events</c>. Works with nothing but <c>new FlowLoggerNode&lt;T&gt;(options)</c> —
/// no engine.
/// </summary>
public sealed class FlowLoggerNode<TInput> : FlowNode<TInput, FlowLogEntry>
{
    public const string NodeType = "flow.logger";
    public const string Emitted = ObservabilityDiagnosticNames.LoggerEmitted;
    public const string Failed = ObservabilityDiagnosticNames.LoggerFailed;

    private readonly FlowLoggerOptions _options;
    private readonly FlowLogLevel _level;
    private readonly IReadOnlyDictionary<string, IObservabilityValueSelector<TInput>> _attributeSelectors;
    private readonly ObservabilityNodeContext _nodeContext;
    private readonly TimeProvider _clock;
    private long _sequence;

    public FlowLoggerNode(
        FlowLoggerOptions options,
        IReadOnlyDictionary<string, IObservabilityValueSelector<TInput>>? attributeSelectors = null,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (options ?? throw new ArgumentNullException(nameof(options))).BoundedCapacity
        })
    {
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Logger bounded capacity must be greater than zero.");
        }

        _options = options;
        _level = options.ResolveLevel();
        _attributeSelectors = attributeSelectors
            ?? new Dictionary<string, IObservabilityValueSelector<TInput>>(StringComparer.Ordinal);
        _clock = clock ?? TimeProvider.System;
        _nodeContext = new ObservabilityNodeContext
        {
            NodeType = NodeType,
            InputType = typeof(TInput),
            Name = _options.EffectiveCategory
        };
    }

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;
        var sequence = Interlocked.Increment(ref _sequence);
        var timestamp = _clock.GetUtcNow();
        var attributes = CreateSelectedAttributes(input, message);
        var messageValues = CreateMessageValues(input, sequence, attributes);
        var entry = new FlowLogEntry
        {
            Timestamp = timestamp,
            Level = _level,
            Category = _options.EffectiveCategory,
            Message = ObservabilityNodeSupport.RenderMessage(
                _options.EffectiveMessageTemplate,
                messageValues),
            InputType = _options.InputType,
            Sequence = sequence,
            Attributes = attributes
        };

        // Carry the correlation id forward onto the entry.
        Emit(message.With(entry));
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = Emitted,
            Level = FlowEventLevel.Information,
            Message = "flow.logger emitted entry.",
            Attributes = ObservabilityNodeSupport.CreateAttributes(
                NodeType,
                _options.InputType,
                _options.EffectiveCategory,
                sequence)
        });

        return Task.CompletedTask;
    }

    private Dictionary<string, object?> CreateSelectedAttributes(TInput input, FlowMessage<TInput> message)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, selector) in _attributeSelectors)
        {
            try
            {
                attributes[name] = selector.Select(input, _nodeContext);
            }
            catch (Exception exception)
            {
                EmitError(new FlowError
                {
                    Timestamp = _clock.GetUtcNow(),
                    CorrelationId = message.CorrelationId,
                    Code = ObservabilityErrorCodes.LoggerAttributeSelectorFailed,
                    Message = $"flow.logger failed to select attribute '{name}': {exception.Message}",
                    Context = CreateErrorContext(name),
                    Exception = exception
                });
                EmitEvent(new FlowEvent
                {
                    Timestamp = _clock.GetUtcNow(),
                    CorrelationId = message.CorrelationId,
                    Name = Failed,
                    Level = FlowEventLevel.Error,
                    Message = $"flow.logger failed to select attribute '{name}'.",
                    Attributes = ObservabilityNodeSupport.CreateAttributes(
                        NodeType,
                        _options.InputType,
                        _options.EffectiveCategory)
                });
            }
        }

        return attributes;
    }

    private Dictionary<string, object?> CreateMessageValues(
        TInput input,
        long sequence,
        IReadOnlyDictionary<string, object?> attributes)
    {
        var values = new Dictionary<string, object?>(attributes, StringComparer.Ordinal)
        {
            ["category"] = _options.EffectiveCategory,
            ["inputType"] = _options.InputType,
            ["level"] = _level.ToString(),
            ["sequence"] = sequence,
            ["input"] = input
        };

        return values;
    }

    private string CreateErrorContext(string selector)
    {
        var values = new List<string>
        {
            $"inputType={_options.InputType}",
            $"category={_options.EffectiveCategory}",
            $"selector={selector}"
        };

        return string.Join("; ", values);
    }
}
