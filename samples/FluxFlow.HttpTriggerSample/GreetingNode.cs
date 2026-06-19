using FluxFlow.Components.Http.Contracts;
using FluxFlow.Nodes;
using System.Text;

namespace FluxFlow.HttpTriggerSample;

/// <summary>
/// A hand-written node on the kit — the "graph" behind the HTTP trigger. It turns an
/// inbound request into a reply, carrying the correlation id forward with With(...).
/// No engine, no registry: it is just a FlowNode you new up and link.
/// </summary>
public sealed class GreetingNode : FlowNode<HttpTriggerRequest, HttpTriggerReply>
{
    protected override Task ProcessAsync(FlowMessage<HttpTriggerRequest> message)
    {
        var name = message.Payload.Body is { Length: > 0 }
            ? Encoding.UTF8.GetString(message.Payload.Body)
            : "world";

        Emit(message.With(HttpTriggerReply.Text(
            $"Hello, {name.Trim()}! (correlation {message.CorrelationId})")));
        return Task.CompletedTask;
    }
}
