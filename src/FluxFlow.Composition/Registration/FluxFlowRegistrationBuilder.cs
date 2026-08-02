using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition;

public sealed class FluxFlowRegistrationBuilder
{
    internal FluxFlowRegistrationBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceCollection Services { get; }
}
