using System.Threading.Tasks.Dataflow;
using System.Text.Json;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Serialization.Nodes;

internal sealed class SerializationPipeline<TInput, TOutput> : FlowNode<TInput, TOutput>
{
    private readonly string _nodeType;
    private readonly string _successKind;
    private readonly string _failureKind;
    private readonly string _successEventName;
    private readonly string _failureEventName;
    private readonly TimeProvider _clock;
    private readonly Func<TInput, TOutput> _convert;
    internal SerializationPipeline(
        string nodeType,
        SerializationNodeOptions? options,
        string successKind,
        string failureKind,
        string successEventName,
        string failureEventName,
        Func<SerializationNodeOptions, Func<TInput, TOutput>> converterFactory,
        TimeProvider? clock)
        : base(CreateNodeOptions(options ?? new SerializationNodeOptions(), nodeType))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(successKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(successEventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureEventName);
        ArgumentNullException.ThrowIfNull(converterFactory);

        _nodeType = nodeType;
        Options = ValidateOptions(options ?? new SerializationNodeOptions(), nodeType);
        _successKind = successKind;
        _failureKind = failureKind;
        _successEventName = successEventName;
        _failureEventName = failureEventName;
        _convert = converterFactory(Options);
        _clock = clock ?? TimeProvider.System;
    }

    internal SerializationNodeOptions Options { get; }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage<TInput> message)
        => await EmitAsync(Process(message), Stopping).ConfigureAwait(false);

    private FlowMessage<TOutput> Process(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message.WithError<TOutput>(message.Error!);

        var timestamp = _clock.GetUtcNow();

        try
        {
            if (message.Value is null)
            {
                throw new SerializationFailureException(
                    SerializationErrorCodeNames.MissingInput,
                    $"{_nodeType} requires input.");
            }

            var value = _convert(message.Value);
            PublishEvent(
                message,
                timestamp,
                _successEventName,
                FlowEventLevel.Information,
                $"{_nodeType} converted input.",
                _successKind,
                isError: false,
                errorCode: null);
            return message.With(value);
        }
        catch (SerializationFailureException exception)
        {
            var error = new DataFlowError(
                exception.Code,
                $"{_nodeType} failed: {exception.Message}",
                category: "Serialization",
                isTransient: false,
                details: CreateErrorDetails(message.Value, exception));
            PublishEvent(
                message,
                timestamp,
                _failureEventName,
                FlowEventLevel.Warning,
                error.Message,
                _failureKind,
                isError: true,
                errorCode: exception.Code);
            return message.WithError<TOutput>(error);
        }
    }

    private void PublishEvent(
        FlowMessage<TInput> message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text,
        string resultKind,
        bool isError,
        string? errorCode)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["nodeType"] = _nodeType,
            ["resultKind"] = resultKind,
            ["isError"] = isError,
            ["inputKind"] = DescribeInputKind(message.Value)
        };
        if (errorCode is not null)
            attributes["errorCode"] = errorCode;

        EmitEvent(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = level,
            Message = text,
            Attributes = attributes
        });
    }

    private JsonElement CreateErrorDetails(TInput? input, Exception exception)
    {
        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["nodeType"] = _nodeType,
            ["inputKind"] = DescribeInputKind(input),
            ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name
        };

        if (input is FlowContent content)
        {
            details["contentType"] = content.ContentType;
            details["encoding"] = content.Encoding;
            details["byteCount"] = content.Bytes.Length;
        }

        return JsonSerializer.SerializeToElement(details);
    }

    private static string DescribeInputKind(TInput? input)
        => input switch
        {
            null => "null",
            JsonElement value => $"Json{value.ValueKind}",
            FlowContent => "ContentBytes",
            _ => typeof(TInput).Name
        };

    private static SerializationNodeOptions ValidateOptions(
        SerializationNodeOptions options,
        string nodeType)
    {
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
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            throw new ArgumentException(
                $"{nodeType} option 'defaultEncoding' is not supported.",
                nameof(options),
                exception);
        }

        return options;
    }

    private static FlowNodeOptions CreateNodeOptions(
        SerializationNodeOptions options,
        string nodeType)
    {
        var validated = ValidateOptions(options, nodeType);
        return new FlowNodeOptions
        {
            InputCapacity = validated.BoundedCapacity,
            OutputCapacity = validated.BoundedCapacity
        };
    }
}
