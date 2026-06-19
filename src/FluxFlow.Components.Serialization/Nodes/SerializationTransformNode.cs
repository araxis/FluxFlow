using FluxFlow.Components.Serialization.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>
/// Base for the serialization/encoding nodes: a standalone <see cref="FlowNode{TInput, TOutput}"/>
/// that runs a synchronous conversion over each message's payload. Post a
/// <c>FlowMessage&lt;TInput&gt;</c> to <c>Input</c>; the node runs the conversion and
/// broadcasts a <c>FlowMessage&lt;TOutput&gt;</c> on <c>Output</c> carrying the same
/// correlation id. A failed conversion surfaces a <see cref="FlowError"/> on the
/// <c>Errors</c> port (with the existing error-code constant) and the node continues
/// with later messages; success and failure also emit a <see cref="FlowEvent"/> on the
/// <c>Events</c> port. No engine, registry, or runtime — just <c>new</c> the node and
/// <c>LinkTo</c> the next one.
/// </summary>
public abstract class SerializationTransformNode<TInput, TOutput>
    : FlowNode<TInput, TOutput>
{
    private readonly string _nodeType;
    private readonly SerializationNodeOptions _options;
    private readonly Func<TInput, SerializationNodeOptions, TOutput> _convert;
    private readonly int _failureCode;
    private readonly string _successEventName;
    private readonly string _failureEventName;
    private readonly Func<TInput, IReadOnlyDictionary<string, object?>> _inputAttributes;
    private readonly Func<TOutput, IReadOnlyDictionary<string, object?>> _outputAttributes;
    private readonly TimeProvider _clock;

    protected SerializationTransformNode(
        string nodeType,
        SerializationNodeOptions options,
        Func<TInput, SerializationNodeOptions, TOutput> convert,
        int failureCode,
        string successEventName,
        string failureEventName,
        Func<TInput, IReadOnlyDictionary<string, object?>> inputAttributes,
        Func<TOutput, IReadOnlyDictionary<string, object?>> outputAttributes,
        TimeProvider? clock = null)
        : base(BuildFlowOptions(options, nodeType))
    {
        _nodeType = nodeType;
        _options = options!;
        _convert = convert ?? throw new ArgumentNullException(nameof(convert));
        _failureCode = failureCode;
        _successEventName = successEventName
            ?? throw new ArgumentNullException(nameof(successEventName));
        _failureEventName = failureEventName
            ?? throw new ArgumentNullException(nameof(failureEventName));
        _inputAttributes = inputAttributes ?? throw new ArgumentNullException(nameof(inputAttributes));
        _outputAttributes = outputAttributes ?? throw new ArgumentNullException(nameof(outputAttributes));
        _clock = clock ?? TimeProvider.System;
    }

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;
        ArgumentNullException.ThrowIfNull(input);

        TOutput result;
        try
        {
            result = _convert(input, _options);
        }
        catch (SerializationNodeException exception)
        {
            ReportConversionError(
                exception.Code,
                exception.Message,
                message,
                exception.InnerException);
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            ReportConversionError(
                _failureCode,
                $"{_nodeType} failed: {exception.Message}",
                message,
                exception);
            return Task.CompletedTask;
        }

        // Carry the correlation id forward onto the converted result.
        Emit(message.With(result));
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = _successEventName,
            Level = FlowEventLevel.Information,
            Message = $"{_nodeType} converted input.",
            Attributes = _outputAttributes(result)
        });
        return Task.CompletedTask;
    }

    private void ReportConversionError(
        int code,
        string message,
        FlowMessage<TInput> source,
        Exception? exception)
    {
        var attributes = _inputAttributes(source.Payload);
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = CreateErrorContext(attributes),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = _failureEventName,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = attributes
        });
    }

    private string CreateErrorContext(IReadOnlyDictionary<string, object?> attributes)
    {
        var values = new List<string>
        {
            $"nodeType={_nodeType}"
        };
        foreach (var attribute in attributes)
        {
            if (attribute.Value is not null)
            {
                values.Add($"{attribute.Key}={attribute.Value}");
            }
        }

        return string.Join("; ", values);
    }

    private static FlowNodeOptions BuildFlowOptions(SerializationNodeOptions options, string nodeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        ArgumentNullException.ThrowIfNull(options);

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"{nodeType} option 'boundedCapacity' must be greater than zero.");
        }

        if (options.MaxInputBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"{nodeType} option 'maxInputBytes' must be greater than zero.");
        }

        if (options.MaxOutputBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"{nodeType} option 'maxOutputBytes' must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultEncoding))
        {
            throw new ArgumentException(
                $"{nodeType} option 'defaultEncoding' must not be empty.",
                nameof(options));
        }

        try
        {
            System.Text.Encoding.GetEncoding(options.DefaultEncoding);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                $"{nodeType} option 'defaultEncoding' is not supported.",
                nameof(options),
                exception);
        }

        return new FlowNodeOptions
        {
            InputCapacity = options.BoundedCapacity,
            MaxDegreeOfParallelism = 1
        };
    }
}
