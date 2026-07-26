using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Diagnostics;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mapping.Nodes;

/// <summary>
/// Maps typed values through a compiled expression and emits mapping failures
/// through the normal output contract.
/// </summary>
public class FlowMapperNode<TInput, TOutput> : IFlowNode
{
    public const string MapperSucceeded = MappingDiagnosticNames.MapperSucceeded;
    public const string MapperFailed = MappingDiagnosticNames.MapperFailed;

    private readonly IFlowMapper<TInput, TOutput> _mapper;
    private readonly IMappingContextFactory _contextFactory;
    private readonly MappingNodeContext _nodeContext;
    private readonly MapperOptions _options;
    private readonly string _engineName;
    private readonly TimeProvider _clock;
    private readonly TransformBlock<FlowMessage<TInput>, FlowMessage<TOutput>> _processor;
    private readonly BroadcastBlock<FlowMessage<TOutput>> _output = new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public FlowMapperNode(
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
            InputType = typeof(TInput),
            OutputType = typeof(TOutput)
        };
        _mapper = new ExpressionFlowMapper<TInput, TOutput>(_options.Expression!, expressionEngine);
        _processor = new TransformBlock<FlowMessage<TInput>, FlowMessage<TOutput>>(
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

    public ITargetBlock<FlowMessage<TInput>> Input => _processor;

    public ISourceBlock<FlowMessage<TOutput>> Output => _output;

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

    private FlowMessage<TOutput> Process(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message.WithError<TOutput>(message.Error!);

        var timestamp = _clock.GetUtcNow();
        try
        {
            var context = _contextFactory.Create(message.Value, _nodeContext);
            var value = _mapper.Map(message.Value, context);
            PublishEvent(
                message,
                timestamp,
                MapperSucceeded,
                FlowEventLevel.Information,
                "Mapped workflow value.");
            return message.With(value);
        }
        catch (Exception exception)
        {
            var error = new FlowError(
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
            return message.WithError<TOutput>(error);
        }
    }

    private void PublishEvent(
        FlowMessage<TInput> message,
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
            ["inputType"] = typeof(TInput).FullName ?? typeof(TInput).Name,
            ["outputType"] = typeof(TOutput).FullName ?? typeof(TOutput).Name,
            ["engine"] = _engineName
        };
        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
            attributes["expressionId"] = _options.ExpressionId;
        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
            attributes["expressionName"] = _options.ExpressionName;
        return attributes;
    }

    private JsonElement CreateErrorDetails(Exception exception)
    {
        var details = CreateAttributes();
        details["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name;
        return JsonSerializer.SerializeToElement(details);
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
            throw new ArgumentOutOfRangeException(nameof(options), "Mapper capacity must be positive.");
        if (string.IsNullOrWhiteSpace(options.Expression))
            throw new ArgumentException("flow.mapper requires an expression.", nameof(options));
        return options;
    }
}

/// <summary>
/// JSON-oriented mapper used by configuration composition.
/// </summary>
public sealed class JsonMapperNode : FlowMapperNode<JsonElement, JsonElement>
{
    public JsonMapperNode(
        MapperOptions options,
        IFlowExpressionEngine expressionEngine,
        IMappingContextFactory? contextFactory = null,
        TimeProvider? clock = null)
        : base(options, expressionEngine, contextFactory, clock)
    {
    }
}
