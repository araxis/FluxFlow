using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Serialization.Nodes;

/// <summary>
/// Base for canonical serialization nodes that emit expected conversion
/// failures as normal <see cref="FlowResult{T}"/> values.
/// </summary>
public abstract class FlowSerializationNode<TInput, TOutput> : IFlowNode
{
    private readonly string _nodeType;
    private readonly string _successKind;
    private readonly string _failureKind;
    private readonly string _successEventName;
    private readonly string _failureEventName;
    private readonly TimeProvider _clock;
    private readonly TransformBlock<
        FlowMessage<TInput>,
        FlowMessage<FlowResult<TOutput>>> _processor;
    private readonly BroadcastBlock<FlowMessage<FlowResult<TOutput>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    private protected FlowSerializationNode(
        string nodeType,
        SerializationNodeOptions? options,
        string successKind,
        string failureKind,
        string successEventName,
        string failureEventName,
        TimeProvider? clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(successKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(successEventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureEventName);

        _nodeType = nodeType;
        Options = ValidateOptions(options ?? new SerializationNodeOptions(), nodeType);
        _successKind = successKind;
        _failureKind = failureKind;
        _successEventName = successEventName;
        _failureEventName = failureEventName;
        _clock = clock ?? TimeProvider.System;
        _processor = new TransformBlock<
            FlowMessage<TInput>,
            FlowMessage<FlowResult<TOutput>>>(
                Process,
                new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = Options.BoundedCapacity,
                    MaxDegreeOfParallelism = 1,
                    EnsureOrdered = true
                });
        _processor.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        _ = MonitorCompletionAsync();
    }

    private protected SerializationNodeOptions Options { get; }

    public ITargetBlock<FlowMessage<TInput>> Input => _processor;

    public ISourceBlock<FlowMessage<FlowResult<TOutput>>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public void Complete() => _processor.Complete();

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ((IDataflowBlock)_processor).Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Complete();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative fault surface.
        }
    }

    private protected abstract TOutput Convert(TInput input);

    private FlowMessage<FlowResult<TOutput>> Process(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var timestamp = _clock.GetUtcNow();

        try
        {
            if (message.Payload is null)
            {
                throw new FlowSerializationException(
                    SerializationErrorCodeNames.MissingInput,
                    $"{_nodeType} requires input.");
            }

            var value = Convert(message.Payload);
            PublishEvent(
                message,
                timestamp,
                _successEventName,
                FlowEventLevel.Information,
                $"{_nodeType} converted input.",
                _successKind,
                isError: false,
                errorCode: null);
            return message.With(FlowResult<TOutput>.Success(
                _successKind,
                value,
                timestamp));
        }
        catch (FlowSerializationException exception)
        {
            var error = new DataFlowError(
                exception.Code,
                $"{_nodeType} failed: {exception.Message}",
                category: "Serialization",
                isTransient: false,
                details: CreateErrorDetails(message.Payload, exception));
            PublishEvent(
                message,
                timestamp,
                _failureEventName,
                FlowEventLevel.Warning,
                error.Message,
                _failureKind,
                isError: true,
                errorCode: exception.Code);
            return message.With(FlowResult<TOutput>.Failure(
                _failureKind,
                error,
                timestamp));
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
            ["inputKind"] = DescribeInputKind(message.Payload)
        };
        if (errorCode is not null)
            attributes["errorCode"] = errorCode;

        _events.Post(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = level,
            Message = text,
            Attributes = attributes
        });
    }

    private FlowValue CreateErrorDetails(TInput? input, Exception exception)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["nodeType"] = FlowValue.From(_nodeType),
            ["inputKind"] = FlowValue.From(DescribeInputKind(input)),
            ["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name)
        };

        if (input is FlowContent content)
        {
            details["contentType"] = OptionalValue(content.ContentType);
            details["encoding"] = OptionalValue(content.Encoding);
            details["byteCount"] = FlowValue.From(
                content.HasOriginalRepresentation ? content.OriginalBytes.Length : 0);
        }

        return FlowValue.FromObject(details);
    }

    private static string DescribeInputKind(TInput? input)
        => input switch
        {
            null => "null",
            FlowValue value => value.Kind.ToString(),
            FlowContent content when content.HasOriginalRepresentation => "ContentBytes",
            FlowContent => "ContentValue",
            _ => typeof(TInput).Name
        };

    private static FlowValue OptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? FlowValue.Null : FlowValue.From(value.Trim());

    private async Task MonitorCompletionAsync()
    {
        try
        {
            await _processor.Completion.ConfigureAwait(false);
            await _output.Completion.ConfigureAwait(false);
            _events.Complete();
            await _events.Completion.ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            _events.Complete();
            _completion.TrySetException(exception);
        }
    }

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
}
