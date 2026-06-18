using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
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
public sealed class HttpTriggerNode : IAsyncDisposable
{
    private readonly RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply> _coordinator;
    private readonly IDisposable _link;

    public HttpTriggerNode(
        ISourceBlock<IRequestContext<HttpTriggerRequest, HttpTriggerReply>> requests,
        RequestReplyOptions? options = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        _coordinator = new RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply>(options, clock);
        _link = requests.LinkTo(
            _coordinator.Incoming,
            new DataflowLinkOptions { PropagateCompletion = true });
    }

    /// <summary>Inbound requests for the graph to handle.</summary>
    public ISourceBlock<FlowMessage<HttpTriggerRequest>> Output => _coordinator.Output;

    /// <summary>Where the graph posts the correlated reply.</summary>
    public ITargetBlock<FlowMessage<HttpTriggerReply>> Responses => _coordinator.Responses;

    public ISourceBlock<FlowError> Errors => _coordinator.Errors;

    public ISourceBlock<FlowEvent> Events => _coordinator.Events;

    public Task Completion => _coordinator.Completion;

    public void Complete() => _coordinator.Complete();

    public async ValueTask DisposeAsync()
    {
        _link.Dispose();
        await _coordinator.DisposeAsync().ConfigureAwait(false);
    }
}
