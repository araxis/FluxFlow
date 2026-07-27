using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Tests;

internal static class ApplicationRuntimeAssemblerTestServiceCollectionExtensions
{
    public static IServiceCollection AddTestRuntimeAssembler(
        this IServiceCollection services,
        Action<IServiceCollection> registerComponents,
        Action<ApplicationResourceRegistrationContext>? registerResources = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registerComponents);

        registerComponents(services);
        if (registerResources is not null)
        {
            services.AddApplicationResourceRegistrar(
                new TestApplicationResourceRegistrar(registerResources));
        }
        return services;
    }

    private sealed class TestApplicationResourceRegistrar(
        Action<ApplicationResourceRegistrationContext> register)
        : IApplicationResourceRegistrar
    {
        public void Register(ApplicationResourceRegistrationContext context) => register(context);
    }
}
