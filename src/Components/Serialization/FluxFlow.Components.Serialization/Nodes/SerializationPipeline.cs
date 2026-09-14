using System.Text.Json;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Serialization.Nodes;

internal sealed class SerializationPipeline<TInput, TOutput> : FlowNode
{
    private readonly string _nodeType;
    private readonly string _successKind;
    private readonly string _failureKind;
    private readonly string _successEventName;
    private readonly string _failureEventName;
    private readonly TimeProvider _clock;
    private readonly Func<FlowValue, TInput> _readInput;
    private readonly Func<TInput, TOutput> _convert;
    internal SerializationPipeline(
        string nodeType,
        SerializationNodeOptions? options,
        string successKind,
        string failureKind,
        string successEventName,
        string failureEventName,
        Func<FlowValue, TInput> readInput,
        Func<SerializationNodeOptions, Func<TInput, TOutput>> converterFactory,
        TimeProvider? clock)
        : base(CreateNodeOptions(options ?? new SerializationNodeOptions(), nodeType))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(successKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(successEventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureEventName);
        ArgumentNullException.ThrowIfNull(readInput);
        ArgumentNullException.ThrowIfNull(converterFactory);

        _nodeType = nodeType;
        _successKind = successKind;
        _failureKind = failureKind;
        _successEventName = successEventName;
        _failureEventName = failureEventName;
        _readInput = readInput;
        _convert = converterFactory(options ?? new SerializationNodeOptions());
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task ProcessAsync(FlowMessage message)
        => await EmitAsync(Process(message), Stopping).ConfigureAwait(false);

    private FlowMessage Process(FlowMessage message)
    {
        var timestamp = _clock.GetUtcNow();
        TInput inputValue;
        JsonValueKind? inputKind = null;
        try
        {
            inputKind = message.Value.Json.ValueKind;
            inputValue = _readInput(message.Value);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            FlowDiagnostics.ProcessingException(message, this, exception);
            var error = typeof(TInput) == typeof(string)
                ? new FlowError("flow.value.expected_string", "The workflow value must be a string.", "Materialization",
                    details: JsonSerializer.SerializeToElement(new { target = "string", actual = (inputKind?.ToString() ?? "Unavailable") }))
                : new FlowError("flow.value.invalid_content", "The workflow value cannot be materialized as exact content.", "Materialization",
                    details: JsonSerializer.SerializeToElement(new { target = nameof(FlowContent), reason = exception.Message }));
            PublishEvent(
                message,
                timestamp,
                $"{_nodeType}.rejected",
                FlowEventLevel.Warning,
                error.Message,
                _failureKind,
                isError: true,
                errorCode: error.Code,
                (inputKind?.ToString() ?? "Unavailable"));
            return message.WithError(error);
        }

        try
        {
            var value = _convert(inputValue);
            PublishEvent(
                message,
                timestamp,
                _successEventName,
                FlowEventLevel.Information,
                $"{_nodeType} converted input.",
                _successKind,
                isError: false,
                errorCode: null,
                DescribeInputKind(inputValue));
            return message.With(value is FlowValue typedValue ? typedValue
                : value is JsonElement json ? new JsonFlowValue(json)
                : value is JsonDocument document ? new JsonFlowValue(document.RootElement)
                : new JsonFlowValue(JsonSerializer.SerializeToElement(value, ValueJsonOptions)));
        }
        catch (SerializationFailureException exception)
        {
            FlowDiagnostics.ProcessingException(message, this, exception);
            var error = new DataFlowError(
                exception.Code,
                $"{_nodeType} failed: {exception.Message}",
                category: "Serialization",
                isTransient: false,
                details: CreateErrorDetails(inputValue, exception));
            PublishEvent(
                message,
                timestamp,
                _failureEventName,
                FlowEventLevel.Warning,
                error.Message,
                _failureKind,
                isError: true,
                errorCode: exception.Code,
                DescribeInputKind(inputValue));
            return message.WithError(error);
        }
    }

    private void PublishEvent(
        FlowMessage message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text,
        string resultKind,
        bool isError,
        string? errorCode,
        string inputKind)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["nodeType"] = _nodeType,
            ["resultKind"] = resultKind,
            ["isError"] = isError,
            ["inputKind"] = inputKind
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
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);
}
