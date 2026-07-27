using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using Microsoft.Extensions.DependencyInjection;
using ApplicationResourceRegistrationContext = FluxFlow.Composition.ApplicationResourceRegistrationContext;
using IApplicationResourceRegistrar = FluxFlow.Composition.IApplicationResourceRegistrar;

namespace FluxFlow.Testing;

public sealed class CanonicalApplicationTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private CanonicalApplicationTestHost(
        ServiceProvider provider,
        ApplicationRevisionLoadResult startResult)
    {
        _provider = provider;
        StartResult = startResult;
        RevisionHost = provider.GetRequiredService<IApplicationRevisionHost>();
        RuntimeAccess = provider.GetRequiredService<IApplicationRuntimeAccess>();
    }

    public IServiceProvider Services => _provider;

    public IApplicationRevisionHost RevisionHost { get; }

    public IApplicationRuntimeAccess RuntimeAccess { get; }

    public ApplicationRevisionLoadResult StartResult { get; }

    public ApplicationPortRuntime GetRequiredPorts() => RuntimeAccess.GetRequiredPorts();

    public static async ValueTask<CanonicalApplicationTestHost> StartAsync(
        ApplicationDefinition definition,
        Action<IServiceCollection> addComponents,
        Action<IServiceCollection>? configureHostServices = null,
        Action<ApplicationResourceRegistrationContext>? registerResources = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(addComponents);

        var services = new ServiceCollection();
        configureHostServices?.Invoke(services);
        services.AddFluxFlowApplication(definition);
        services.AddFluxFlowEngine();
        addComponents(services);
        if (registerResources is not null)
        {
            ApplicationResourceServiceCollectionExtensions.AddApplicationResourceRegistrar(
                services,
                new TestApplicationResourceRegistrar(registerResources));
        }

        var provider = services.BuildServiceProvider();
        try
        {
            var startResult = await provider
                .GetRequiredService<IApplicationRevisionHost>()
                .StartApplicationAsync(cancellationToken)
                .ConfigureAwait(false);
            return new CanonicalApplicationTestHost(provider, startResult);
        }
        catch
        {
            await provider.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    private sealed class TestApplicationResourceRegistrar(
        Action<ApplicationResourceRegistrationContext> register)
        : IApplicationResourceRegistrar
    {
        public void Register(ApplicationResourceRegistrationContext context) => register(context);
    }
}
