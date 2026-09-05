using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.FileSystem.Nodes;

internal sealed class FileSystemOperationPipeline<TInput, TOutput> : FlowNode
{
    private readonly TimeProvider _clock;
    private readonly IFlowValueMaterializer<TInput> _materializer;
    private readonly string _rejectionEventName;
    private readonly Func<FlowMessage<TInput>, CancellationToken, Task<FlowMessage<TOutput>>> _process;

    public FileSystemOperationPipeline(
        int boundedCapacity,
        TimeProvider clock,
        IFlowValueMaterializer<TInput> materializer,
        string rejectionEventName,
        Func<FlowMessage<TInput>, CancellationToken, Task<FlowMessage<TOutput>>> process)
        : base(CreateOptions(boundedCapacity))
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(materializer);
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectionEventName);
        ArgumentNullException.ThrowIfNull(process);
        _clock = clock;
        _materializer = materializer;
        _rejectionEventName = rejectionEventName;
        _process = process;
    }

    public void PublishEvent(FlowEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        EmitEvent(@event);
    }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage message)
    {
        if (message.IsError)
        {
            await EmitAsync(message, Stopping).ConfigureAwait(false);
            return;
        }

        var materialized = _materializer.Materialize(message.Value);
        if (!materialized.IsSuccess)
        {
            var error = materialized.Error!;
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = _rejectionEventName,
                Level = FlowEventLevel.Warning,
                Message = error.Message,
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["errorCode"] = error.Code
                }
            });
            await EmitAsync(message.WithError(error), Stopping).ConfigureAwait(false);
            return;
        }

        var typedMessage = message.ConvertValue(materialized.Value);
        var typedResult = await _process(typedMessage, Stopping).ConfigureAwait(false);
        var result = typedResult.IsError
            ? typedResult.ConvertError()
            : typedResult.ConvertValue(FlowValue.From(typedResult.Value));
        await EmitAsync(result, Stopping).ConfigureAwait(false);
    }

    private static FlowNodeOptions CreateOptions(int boundedCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedCapacity, 1);
        return new FlowNodeOptions
        {
            InputCapacity = boundedCapacity,
            OutputCapacity = boundedCapacity
        };
    }
}
