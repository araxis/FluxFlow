using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using Microsoft.Extensions.DependencyInjection;

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
        Action<CompositionNodeRegistry> registerNodes,
        Action<IServiceCollection>? configureHostServices = null,
        Action<ApplicationRuntimeServicesContext>? configureRuntimeServices = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(registerNodes);

        var services = new ServiceCollection();
        configureHostServices?.Invoke(services);
        services
            .AddFluxFlowApplication(definition)
            .UseRuntimeAssembler(runtime =>
            {
                runtime.RegisterNodes(registerNodes);
                if (configureRuntimeServices is not null)
                    runtime.ConfigureServices(configureRuntimeServices);
            });

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
}
