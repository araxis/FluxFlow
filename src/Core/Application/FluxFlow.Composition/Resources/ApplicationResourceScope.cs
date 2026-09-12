using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition;

/// <summary>Owns constructed resource services and borrows the caller's fallback services.</summary>
internal sealed class ApplicationResourceScope : IServiceProvider, IKeyedServiceProvider, IAsyncDisposable
{
    private ServiceProvider? _services;
    private readonly IServiceProvider _host;

    internal ApplicationResourceScope(IServiceCollection services, IServiceProvider host)
    {
        _host = host;
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private ServiceProvider Services => Volatile.Read(ref _services) ?? throw new ObjectDisposedException(nameof(ApplicationResourceScope));

    public object? GetService(Type serviceType) => Services.GetService(serviceType) ?? _host.GetService(serviceType);
    public object? GetKeyedService(Type serviceType, object? serviceKey)
        => Services.GetKeyedService(serviceType, serviceKey) ?? (_host as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);
    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
        => GetKeyedService(serviceType, serviceKey) ?? throw new InvalidOperationException($"No service for type '{serviceType}' and key '{serviceKey}' has been registered.");

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _services, null) is { } services) await services.DisposeAsync().ConfigureAwait(false);
    }
}
