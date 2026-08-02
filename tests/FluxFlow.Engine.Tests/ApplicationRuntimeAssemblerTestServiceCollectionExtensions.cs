using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Tests;

internal static class ApplicationRuntimeAssemblerTestServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddTestRuntimeAssembler(
        this FluxFlowRegistrationBuilder builder,
        Action<IServiceCollection> registerComponents,
        Action<ApplicationResourceRegistrationContext>? registerResources = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(registerComponents);

        var services = builder.Services;
        registerComponents(services);
        if (registerResources is not null)
        {
            services.AddApplicationResourceRegistrar(
                new TestApplicationResourceRegistrar(registerResources));
        }
        return builder;
    }

    private sealed class TestApplicationResourceRegistrar(
        Action<ApplicationResourceRegistrationContext> register)
        : IApplicationResourceRegistrar
    {
        public void Register(ApplicationResourceRegistrationContext context) => register(context);
    }
}
