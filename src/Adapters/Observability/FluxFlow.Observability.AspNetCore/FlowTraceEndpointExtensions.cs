using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Observability;

public static class FlowTraceEndpointExtensions
{
    public static IServiceCollection AddFluxFlowTraceStream(this IServiceCollection services)
    {
        services.TryAddSingleton<FlowTraceStream>();
        return services;
    }

    /// <summary>Maps a live process-wide trace stream. Apply the host's authorization policy to this endpoint.</summary>
    public static RouteHandlerBuilder MapFluxFlowTraceStream(this IEndpointRouteBuilder endpoints,
        string pattern = "/fluxflow/traces", int capacityPerClient = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityPerClient);
        return endpoints.MapGet(pattern, (FlowTraceStream stream, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            return TypedResults.ServerSentEvents(stream.ReadAsync(capacityPerClient, context.RequestAborted),
                eventType: "fluxflow.span");
        });
    }
}
