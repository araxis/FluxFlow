using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
using FluxFlow.Data;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Http.AspNetCore;

/// <summary>
/// The HTTP trigger as a component. It is given its inbound request source (injected,
/// keyed) and uses a <see cref="RequestReplyCoordinator{TRequest,TResponse}"/> to
/// correlate replies back to callers. It exposes the graph-facing ports: requests on
/// <see cref="Output"/>, responses back on <see cref="Responses"/>. The endpoint and the
/// transport never appear here — only the request source and the coordinator.
/// </summary>
public sealed class HttpTriggerNode : IFlowNode
{
    private readonly RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply> _coordinator;
    private readonly FlowOutput<FlowMessage> _output;
    private readonly TimeProvider _clock;
    private readonly TransformBlock<FlowMessage, FlowMessage<HttpTriggerReply>> _responses;
    private readonly ActionBlock<FlowMessage<HttpTriggerRequest>> _outputPump;
    private readonly BroadcastBlock<FlowMessage> _events = new(static @event => @event);
    private readonly ActionBlock<FlowMessage> _eventRelay;
    private readonly IDisposable _requestLink;
    private readonly IDisposable _responseLink;
    private readonly IDisposable _outputLink;
    private readonly IDisposable _eventLink;
    private readonly Task _completion;
    private readonly HttpTriggerReplyMaterializer _replyMaterializer = new();
    private int _disposed;

    public HttpTriggerNode(
        ISourceBlock<IRequestContext<HttpTriggerRequest, HttpTriggerReply>> requests,
        RequestReplyOptions? options = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var capacity = options?.Capacity ?? 128;
        _clock = clock ?? TimeProvider.System;
        _coordinator = new RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply>(options, _clock);
        _output = new FlowOutput<FlowMessage>(
            new FlowOutputOptions { Capacity = capacity });
        _responses = new TransformBlock<FlowMessage, FlowMessage<HttpTriggerReply>>(
            MaterializeReply,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = capacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _outputPump = new ActionBlock<FlowMessage<HttpTriggerRequest>>(
            CanonicalizeRequestAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = capacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _eventRelay = new ActionBlock<FlowMessage>(@event => _events.Post(@event));

        _requestLink = requests.LinkTo(
            _coordinator.Incoming,
            new DataflowLinkOptions { PropagateCompletion = true });
        _responseLink = _responses.LinkTo(
            _coordinator.Responses,
            new DataflowLinkOptions { PropagateCompletion = true });
        _outputLink = _coordinator.Output.LinkTo(
            _outputPump,
            new DataflowLinkOptions { PropagateCompletion = true });
        _eventLink = _coordinator.Events.LinkTo(
            _eventRelay,
            new DataflowLinkOptions { PropagateCompletion = true });
        _completion = CompleteAsync();
    }

    /// <summary>Inbound requests for the graph to handle.</summary>
    public ISourceBlock<FlowMessage> Output => _output;

    /// <summary>Where the graph posts the correlated reply.</summary>
    public ITargetBlock<FlowMessage> Responses => _responses;

    public ISourceBlock<FlowMessage> Events => _events;

    public Task Completion => _completion;

    public void Complete()
    {
        _responses.Complete();
        _coordinator.Complete();
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ((IDataflowBlock)_responses).Fault(exception);
        _coordinator.Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Complete();
        try
        {
            await _completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative fault surface.
        }
        finally
        {
            _requestLink.Dispose();
            _responseLink.Dispose();
            _outputLink.Dispose();
            _eventLink.Dispose();
            await _coordinator.DisposeAsync().ConfigureAwait(false);
            await _output.DisposeAsync().ConfigureAwait(false);
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

    private async Task CanonicalizeRequestAsync(FlowMessage<HttpTriggerRequest> message)
    {
        var canonical = message.IsError
            ? message.ConvertError()
            : message.ConvertValue(FlowValue.From(message.Value));
        if (!await _output.SendAsync(canonical).ConfigureAwait(false))
            throw new InvalidOperationException("HTTP trigger output is unavailable.");
    }

    private async Task CompleteAsync()
    {
        try
        {
            await _coordinator.Completion.ConfigureAwait(false);
            await _outputPump.Completion.ConfigureAwait(false);
            await _eventRelay.Completion.ConfigureAwait(false);
            _output.Complete();
            _events.Complete();
            await Task.WhenAll(_output.Completion, _events.Completion).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _output.Fault(exception);
            _events.Complete();
            throw;
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
