using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Http.AspNetCore;

public static class FluxFlowTriggerEndpointExtensions
{
    /// <summary>
    /// Maps an endpoint to the trigger registered under <paramref name="name"/> via
    /// <c>AddFluxFlowHttpTrigger</c>. The endpoint feeds the keyed request source and
    /// holds the response open until the graph answers (or the trigger times it out → 504).
    /// </summary>
    public static IEndpointConventionBuilder MapFluxFlowTrigger(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string name,
        string? correlationHeader = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var source = endpoints.ServiceProvider.GetRequiredKeyedService<HttpTriggerSource>(name.Trim());
        return MapCore(endpoints, pattern, (request, ct) => source.SubmitAsync(request, ct), correlationHeader);
    }

    /// <summary>
    /// Maps an endpoint that feeds a coordinator you hold directly — handy for tests and
    /// manual composition without DI.
    /// </summary>
    public static IEndpointConventionBuilder MapFluxFlowTrigger(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply> coordinator,
        string? correlationHeader = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(coordinator);

        return MapCore(endpoints, pattern, (request, ct) => coordinator.Incoming.SendAsync(request, ct), correlationHeader);
    }

    private static IEndpointConventionBuilder MapCore(
        IEndpointRouteBuilder endpoints,
        string pattern,
        Func<IRequestContext<HttpTriggerRequest, HttpTriggerReply>, CancellationToken, Task<bool>> submit,
        string? correlationHeader)
        => endpoints.Map(pattern, async (HttpContext http) =>
        {
            var context = await HttpRequestContext.CreateAsync(http, correlationHeader).ConfigureAwait(false);

            try
            {
                if (!await submit(context, http.RequestAborted).ConfigureAwait(false))
                {
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

            // Hold the response open until the graph (or a timeout) answers through the context.
            await context.Completed.ConfigureAwait(false);
        });
}
