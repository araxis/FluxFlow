using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Observability.Nodes;

/// <summary>
/// Owns the canonical workflow boundary for a typed observability operation.
/// Materialization, output conversion, events, and lifecycle remain inside the
/// user-visible component rather than in a composition-time wrapper.
/// </summary>
internal sealed class CanonicalObservabilityNode<TOutput> : FlowNode
{
    private readonly IFlowNode _operation;
    private readonly ITargetBlock<FlowMessage<JsonElement>> _operationInput;
    private readonly BufferBlock<FlowMessage<TOutput>> _responses;
    private readonly string _nodeType;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<FlowMessage> _eventRelay;
    private readonly IDisposable _outputLink;
    private readonly IDisposable _eventLink;

    internal CanonicalObservabilityNode(
        IFlowNode operation,
        ITargetBlock<FlowMessage<JsonElement>> input,
        ISourceBlock<FlowMessage<TOutput>> output,
        ISourceBlock<FlowMessage> events,
        string nodeType,
        int boundedCapacity,
        TimeProvider? clock)
        : base(new FlowNodeOptions
        {
            InputCapacity = boundedCapacity,
            OutputCapacity = boundedCapacity
        })
    {
        _operation = operation;
        _operationInput = input;
        _nodeType = nodeType;
        _clock = clock ?? TimeProvider.System;
        _responses = new BufferBlock<FlowMessage<TOutput>>(new DataflowBlockOptions
        {
            BoundedCapacity = 1
        });
        _eventRelay = new ActionBlock<FlowMessage>(@event => EmitEvent(@event));
        _outputLink = output.LinkTo(
            _responses,
            new DataflowLinkOptions { PropagateCompletion = true });
        _eventLink = events.LinkTo(
            _eventRelay,
            new DataflowLinkOptions { PropagateCompletion = true });
    }

    protected override async Task ProcessAsync(FlowMessage message)
    {
        var materialization = FlowValueMaterializers.JsonElement.Materialize(message.Value!);
        if (!materialization.IsSuccess)
        {
            var error = materialization.Error!;
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = $"{_nodeType}.rejected",
                Level = FlowEventLevel.Warning,
                Message = error.Message,
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["errorCode"] = error.Code,
                    ["isError"] = true,
                    ["nodeType"] = _nodeType
                }
            });
            await EmitAsync(message.WithError(error), Stopping).ConfigureAwait(false);
            return;
        }

        if (!await _operationInput.SendAsync(
                message.ConvertValue(materialization.Value),
                Stopping).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"{_nodeType} input is unavailable.");
        }

        var response = await _responses.ReceiveAsync(Stopping).ConfigureAwait(false);
        await EmitAsync(
                response.IsError
                    ? response.ConvertError()
                    : response.ConvertValue(FlowValue.From(response.Value)),
                Stopping)
            .ConfigureAwait(false);
    }

    protected override async ValueTask OnInputCompletedAsync()
    {
        _operation.Complete();
        await _operation.Completion.ConfigureAwait(false);
        await _responses.Completion.ConfigureAwait(false);
        await _eventRelay.Completion.ConfigureAwait(false);
    }

    protected override async ValueTask OnDisposeAsync()
    {
        _outputLink.Dispose();
        _eventLink.Dispose();
        await _operation.DisposeAsync().ConfigureAwait(false);
    }
}
