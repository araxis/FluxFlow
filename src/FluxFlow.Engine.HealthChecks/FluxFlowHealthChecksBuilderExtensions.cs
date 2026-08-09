using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluxFlow.Engine.HealthChecks;

public static class FluxFlowHealthChecksBuilderExtensions
{
    public static IHealthChecksBuilder AddFluxFlowApplication(
        this IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Services.Any(static descriptor =>
                descriptor.ServiceType ==
                typeof(FluxFlowApplicationHealthCheckRegistrationMarker)))
        {
            return builder;
        }

        builder.Services.AddSingleton(
            new FluxFlowApplicationHealthCheckRegistrationMarker());
        builder.Add(new HealthCheckRegistration(
            "fluxflow.application",
            static provider => new FluxFlowApplicationHealthCheck(
                provider.GetService<FluxFlowApplication>()),
            HealthStatus.Unhealthy,
            ["fluxflow", "ready"]));
        return builder;
    }

    private sealed class FluxFlowApplicationHealthCheckRegistrationMarker;
}
