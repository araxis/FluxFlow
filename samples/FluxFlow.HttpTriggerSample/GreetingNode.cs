using FluxFlow.Components.Http.Contracts;
using FluxFlow.Data;
using FluxFlow.Nodes;
using System.Text;

namespace FluxFlow.HttpTriggerSample;

/// <summary>
/// A hand-written node on the kit — the "graph" behind the HTTP trigger. It turns an
/// inbound request into a reply, carrying the correlation id forward with With(...).
/// No engine, no registry: it is just a FlowNode you new up and link.
/// </summary>
public sealed class GreetingNode : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        var request = message.Value!.Deserialize<HttpTriggerRequest>();
        var name = request?.Body is { Length: > 0 }
            ? Encoding.UTF8.GetString(request.Body)
            : "world";

        await EmitAsync(
                message.With(FlowValue.From(HttpTriggerReply.Text(
                    $"Hello, {name.Trim()}! (correlation {message.CorrelationId})"))),
                Stopping)
            .ConfigureAwait(false);
    }
}
