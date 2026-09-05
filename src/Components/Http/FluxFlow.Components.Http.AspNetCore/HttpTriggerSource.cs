using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Http.AspNetCore;

/// <summary>
/// The seam between the ASP.NET endpoint and the trigger: a bounded buffer of inbound
/// request contexts. The endpoint submits to it; the <see cref="HttpTriggerNode"/>
/// consumes it. Registered as a keyed service so they meet by trigger name without
/// referencing each other.
/// </summary>
public sealed class HttpTriggerSource
{
    private readonly BufferBlock<IRequestContext<HttpTriggerRequest, HttpTriggerReply>> _requests;

    public HttpTriggerSource(int capacity = 128)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        _requests = new BufferBlock<IRequestContext<HttpTriggerRequest, HttpTriggerReply>>(
            new DataflowBlockOptions { BoundedCapacity = capacity });
    }

    /// <summary>The request stream the trigger consumes.</summary>
    public ISourceBlock<IRequestContext<HttpTriggerRequest, HttpTriggerReply>> Requests => _requests;

    /// <summary>The endpoint submits an inbound request; backpressures when the buffer is full.</summary>
    public Task<bool> SubmitAsync(
        IRequestContext<HttpTriggerRequest, HttpTriggerReply> request,
        CancellationToken cancellationToken = default)
        => _requests.SendAsync(request, cancellationToken);

    public void Complete() => _requests.Complete();
}
