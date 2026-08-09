using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition;

public sealed class FluxFlowRegistrationBuilder
{
    internal FluxFlowRegistrationBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceCollection Services { get; }

    public AdvancedFluxFlowRegistrationBuilder Advanced => new(Services);
}

public sealed class AdvancedFluxFlowRegistrationBuilder
{
    internal AdvancedFluxFlowRegistrationBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    internal IServiceCollection Services { get; }

    public AdvancedFluxFlowRegistrationBuilder AddDynamicComponent(
        string type,
        Action<RuntimeComponentRegistrationBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(configure);

        var component = new RuntimeComponentRegistrationBuilder(type);
        configure(component);
        FluxFlowRegistrationExtensions.RegisterDescriptor(
            Services,
            component.CreateDescriptor());
        return this;
    }
}
