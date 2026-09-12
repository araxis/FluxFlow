using FluxFlow.Nodes;
using System.Diagnostics;

namespace FluxFlow.Components.RequestReply;

/// <summary>
/// One inbound request plus how to answer it. The host/transport adapter creates
/// these (e.g. from an <c>HttpContext</c> or an MQTT request message): the
/// <see cref="RequestReplyCoordinator{TRequest,TResponse}"/> forwards <see cref="Request"/>
/// into the graph and calls <see cref="ReplyAsync"/> (or <see cref="FailAsync"/> on
/// timeout) when the correlated response comes back. The bridge never sees the
/// transport — only this contract.
/// </summary>
public interface IRequestContext<out TRequest, in TResponse>
{
    TRequest Request { get; }

    /// <summary>
    /// Optional host-supplied correlation id (e.g. an inbound trace/request id). When
    /// null no external correlation was supplied. The graph receives a fresh exchange
    /// correlation for every request; transport contexts may reuse their external id.
    /// </summary>
    CorrelationId? CorrelationId { get; }

    /// <summary>
    /// Optional distributed trace context captured by the transport before queueing.
    /// Default means no parent; a worker's ambient activity is never the request parent.
    /// This is independent of the exchange correlation id.
    /// </summary>
    ActivityContext TraceContext => default;

    /// <summary>Answer the caller. Called once when the correlated response arrives.</summary>
    Task ReplyAsync(TResponse response, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledge that the request was accepted into the graph, with no graph result — the
    /// bridge calls this once in fire-and-forget mode. Transport-specific: HTTP writes an
    /// accepted status; MQTT has nothing to send back.
    /// </summary>
    Task AcknowledgeAsync(CancellationToken cancellationToken = default);

    /// <summary>Answer with a failure — the bridge calls this on timeout or shutdown.</summary>
    Task FailAsync(Exception error, CancellationToken cancellationToken = default);
}
