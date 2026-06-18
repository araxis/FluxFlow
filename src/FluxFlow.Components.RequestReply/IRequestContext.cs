using FluxFlow.Nodes;

namespace FluxFlow.Components.RequestReply;

/// <summary>
/// One inbound request plus how to answer it. The host/transport adapter creates
/// these (e.g. from an <c>HttpContext</c> or an MQTT request message): the
/// <see cref="RequestReplyBridge{TRequest,TResponse}"/> forwards <see cref="Request"/>
/// into the graph and calls <see cref="ReplyAsync"/> (or <see cref="FailAsync"/> on
/// timeout) when the correlated response comes back. The bridge never sees the
/// transport — only this contract.
/// </summary>
public interface IRequestContext<out TRequest, in TResponse>
{
    TRequest Request { get; }

    /// <summary>
    /// Optional host-supplied correlation id (e.g. an inbound trace/request id). When
    /// null the bridge mints one. Distinct contexts must not share a non-null id.
    /// </summary>
    CorrelationId? CorrelationId { get; }

    /// <summary>Answer the caller. Called once when the correlated response arrives.</summary>
    Task ReplyAsync(TResponse response, CancellationToken cancellationToken = default);

    /// <summary>Answer with a failure — the bridge calls this on timeout or shutdown.</summary>
    Task FailAsync(Exception error, CancellationToken cancellationToken = default);
}
