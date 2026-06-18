using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Http.AspNetCore;

public static class FluxFlowTriggerEndpointExtensions
{
    /// <summary>
    /// Maps an endpoint that feeds inbound requests into <paramref name="bridge"/> and
    /// writes the graph's correlated reply back. The endpoint holds the response open
    /// until the graph answers (or the bridge times the request out → 504).
    /// </summary>
    /// <param name="correlationHeader">
    /// Optional request header whose value seeds the correlation id (e.g. an inbound
    /// trace id); when absent the bridge mints one.
    /// </param>
    public static IEndpointConventionBuilder MapFluxFlowTrigger(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        RequestReplyBridge<HttpTriggerRequest, HttpTriggerReply> bridge,
        string? correlationHeader = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(bridge);

        return endpoints.Map(pattern, async (HttpContext http) =>
        {
            var context = await HttpRequestContext.CreateAsync(http, correlationHeader)
                .ConfigureAwait(false);

            try
            {
                if (!await bridge.Incoming.SendAsync(context, http.RequestAborted).ConfigureAwait(false))
                {
                    // The bridge is shutting down / not accepting.
                    if (!http.Response.HasStarted)
                    {
                        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    }

                    return;
                }
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
            {
                return; // client went away before the request was accepted
            }

            // Wait until the graph (or a timeout) writes the response through the context.
            await context.Completed.ConfigureAwait(false);
        });
    }
}
