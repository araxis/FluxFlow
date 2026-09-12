using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
using FluxFlow.Nodes;
using FluxFlow.Data;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Http.AspNetCore;

/// <summary>
/// Bridges host-owned request contexts to canonical graph ports. Typed reply
/// validation stays at this transport boundary; the shared bridge owns execution.
/// </summary>
public sealed class HttpTriggerNode : IFlowSource, IFlowComponent
{
    private readonly RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply> _coordinator;
    private readonly TimeProvider _clock;
    private readonly BroadcastBlock<FlowMessage> _events = new(static message => message);
    private readonly IDisposable _eventLink;
    private readonly Task _completion;
    private readonly HttpTriggerReplyMaterializer _replyMaterializer = new();
    private int _disposed;

    public HttpTriggerNode(
        RequestReplyOptions? options = null,
        TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _coordinator = new RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply>(
            options, _clock, MaterializeReply);
        Ports = new FlowPorts(outputs: _coordinator.Ports.Outputs, events: Events,
            controls: _coordinator.Ports.Controls);
        _eventLink = _coordinator.Events.LinkTo(_events);
        _completion = CompleteAsync();
    }

    public ITargetBlock<IRequestContext<HttpTriggerRequest, HttpTriggerReply>> Incoming => _coordinator.Incoming;
    public ISourceBlock<FlowMessage> Output => _coordinator.Output;
    public ITargetBlock<FlowMessage> Responses => _coordinator.Responses;
    public ISourceBlock<FlowMessage> Events => _events;
    public FlowPorts Ports { get; }
    public Task Completion => _completion;
    public Task ProductionCompletion => _coordinator.ProductionCompletion;
    public Task StartAsync(CancellationToken cancellationToken = default)
        => _coordinator.StartAsync(cancellationToken);
    public void StopProduction() => _coordinator.StopProduction();
    public void Complete() => _coordinator.Complete();
    public void Fault(Exception exception) => _coordinator.Fault(exception);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Complete();
        try { await Completion.ConfigureAwait(false); }
        catch { /* Completion remains the authoritative processing-fault surface. */ }
        finally
        {
            _eventLink.Dispose();
            await _coordinator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private FlowMessage<HttpTriggerReply> MaterializeReply(FlowMessage message)
    {
        if (message.IsError)
            return message.ConvertError<HttpTriggerReply>();

        var materialization = _replyMaterializer.Materialize(message.Value);
        if (materialization.IsSuccess)
            return message.ConvertValue(materialization.Value);

        var error = materialization.Error!;
        _events.Post(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = "http.trigger.reply.rejected",
            Level = FlowEventLevel.Warning,
            Message = error.Message,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["errorCode"] = error.Code,
                ["isError"] = true,
                ["nodeType"] = "http.trigger"
            }
        });
        return message.WithError(error).ConvertError<HttpTriggerReply>();
    }

    private async Task CompleteAsync()
    {
        try { await _coordinator.Completion.ConfigureAwait(false); }
        finally
        {
            _events.Complete();
            await _events.Completion.ConfigureAwait(false);
        }
    }

    private sealed class HttpTriggerReplyMaterializer
        : JsonFlowValueMaterializer<HttpTriggerReply>
    {
        internal HttpTriggerReplyMaterializer()
            : base(
                "http.trigger.reply.invalid_shape",
                "HTTP",
                FlowValueShape.Object(
                    "An HTTP reply object with statusCode, headers, body, and contentType fields."))
        {
        }
    }
}
