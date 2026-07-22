using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Diagnostics;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Mapping.Nodes;

/// <summary>
/// Maps immutable <see cref="FlowValue"/> payloads without serializing them.
/// Expected expression failures are emitted as normal <see cref="FlowResult{T}"/>
/// values so the node has one workflow output.
/// </summary>
public sealed class FlowValueMapperNode : IFlowNode
{
    public const string MapperSucceeded = MappingDiagnosticNames.MapperSucceeded;
    public const string MapperFailed = MappingDiagnosticNames.MapperFailed;

    private readonly IFlowMapper<FlowValue, FlowValue> _mapper;
    private readonly IMappingContextFactory _contextFactory;
    private readonly MappingNodeContext _nodeContext;
    private readonly MapperOptions _options;
    private readonly string _engineName;
    private readonly TimeProvider _clock;
    private readonly TransformBlock<FlowMessage<FlowValue>, FlowMessage<FlowResult<FlowValue>>> _processor;
    private readonly BroadcastBlock<FlowMessage<FlowResult<FlowValue>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public FlowValueMapperNode(
        MapperOptions options,
        IFlowExpressionEngine expressionEngine,
        IMappingContextFactory? contextFactory = null,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(expressionEngine);

        _engineName = expressionEngine.Name;
        _contextFactory = contextFactory ?? DefaultMappingContextFactory.Instance;
        _clock = clock ?? TimeProvider.System;
        _nodeContext = new MappingNodeContext
        {
            Options = _options,
            InputType = typeof(FlowValue),
            OutputType = typeof(FlowValue)
        };
        _mapper = new ExpressionFlowMapper<FlowValue, FlowValue>(
            _options.Expression!,
            expressionEngine);
        _processor = new TransformBlock<FlowMessage<FlowValue>, FlowMessage<FlowResult<FlowValue>>>(
            Process,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _options.BoundedCapacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
        _processor.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<FlowMessage<FlowValue>> Input => _processor;

    public ISourceBlock<FlowMessage<FlowResult<FlowValue>>> Output => _output;

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

    private FlowMessage<FlowResult<FlowValue>> Process(FlowMessage<FlowValue> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var timestamp = _clock.GetUtcNow();

        try
        {
            var context = _contextFactory.Create(message.Payload, _nodeContext);
            var value = _mapper.Map(message.Payload, context)
                ?? throw new InvalidOperationException(
                    "The mapping expression returned no FlowValue.");
            PublishEvent(
                message,
                timestamp,
                MapperSucceeded,
                FlowEventLevel.Information,
                "Mapped workflow value.");
            return message.With(FlowResult<FlowValue>.Success(
                MappingResultKinds.Mapped,
                value,
                timestamp));
        }
        catch (Exception exception)
        {
            var error = new DataFlowError(
                MappingErrorCodeNames.MapperFailed,
                $"flow.mapper failed to map input: {exception.Message}",
                category: "Mapping",
                isTransient: false,
                details: CreateErrorDetails(exception));
            PublishEvent(
                message,
                timestamp,
                MapperFailed,
                FlowEventLevel.Warning,
                error.Message);
            return message.With(FlowResult<FlowValue>.Failure(
                MappingResultKinds.Failed,
                error,
                timestamp,
                message.Payload));
        }
    }

    private void PublishEvent(
        FlowMessage<FlowValue> message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text)
        => _events.Post(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = level,
            Message = text,
            Attributes = CreateAttributes()
        });

    private Dictionary<string, object?> CreateAttributes()
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = _options.InputType,
            ["outputType"] = _options.OutputType,
            ["engine"] = _engineName
        };
        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
            attributes["expressionId"] = _options.ExpressionId;
        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
            attributes["expressionName"] = _options.ExpressionName;
        return attributes;
    }

    private FlowValue CreateErrorDetails(Exception exception)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["engine"] = FlowValue.From(_engineName),
            ["exceptionType"] = FlowValue.From(exception.GetType().FullName ?? exception.GetType().Name),
            ["inputType"] = FlowValue.From(_options.InputType),
            ["outputType"] = FlowValue.From(_options.OutputType)
        };
        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
            details["expressionId"] = FlowValue.From(_options.ExpressionId);
        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
            details["expressionName"] = FlowValue.From(_options.ExpressionName);
        return FlowValue.FromObject(details);
    }

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

    private static MapperOptions ValidateOptions(MapperOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Mapper bounded capacity must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.Expression))
            throw new ArgumentException("flow.mapper requires an expression.", nameof(options));
        return options;
    }
}
