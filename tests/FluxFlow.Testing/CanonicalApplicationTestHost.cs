using FluxFlow.Composition;
using FluxFlow.Composition.Model;
using FluxFlow.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Testing;

public sealed class CanonicalApplicationTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private CanonicalApplicationTestHost(
        ServiceProvider provider,
        ApplicationUpdateResult startResult)
    {
        _provider = provider;
        Application = provider.GetRequiredService<FluxFlowApplication>();
        RuntimeAccess = new CanonicalApplicationRuntimeAccess(Application);
        StartResult = new CanonicalApplicationStartResult(startResult);
    }

    public IServiceProvider Services => _provider;

    public FluxFlowApplication Application { get; }

    public FluxFlowApplication RevisionHost => Application;

    public CanonicalApplicationRuntimeAccess RuntimeAccess { get; }

    public CanonicalApplicationStartResult StartResult { get; }

    public ApplicationPorts GetRequiredPorts() => RuntimeAccess.GetRequiredPorts();

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
        services.AddFluxFlow(definition, options => options.StartWithHost = false);
        addComponents(services);
        if (registerResources is not null)
        {
            services.AddApplicationResourceRegistrar(
                new TestApplicationResourceRegistrar(registerResources));
        }

        var provider = services.BuildServiceProvider();
        try
        {
            var startResult = await provider
                .GetRequiredService<FluxFlowApplication>()
                .StartAsync(cancellationToken)
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

public sealed class CanonicalApplicationRuntimeAccess(FluxFlowApplication application)
{
    public ApplicationPorts? Ports => application.Current is null ? null : application.Ports;

    public ApplicationPorts GetRequiredPorts()
        => Ports ?? throw new InvalidOperationException(
            "Application ports are unavailable until the first revision is active.");
}

public sealed record CanonicalApplicationStartResult(ApplicationUpdateResult? Update)
{
    public bool Succeeded => Update is not null && !Update.IsRejected;
}
