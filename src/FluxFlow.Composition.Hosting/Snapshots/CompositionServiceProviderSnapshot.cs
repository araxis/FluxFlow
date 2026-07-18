using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition.Hosting.Snapshots;

public sealed class CompositionServiceProviderSnapshot :
    IServiceProvider,
    IKeyedServiceProvider,
    IDisposable,
    IAsyncDisposable
{
    private IServiceProvider? _provider;
    private readonly bool _ownsProvider;

    internal CompositionServiceProviderSnapshot(
        string name,
        CompositionProviderBoundary boundary,
        IServiceProvider provider,
        bool ownsProvider,
        int? serviceCount)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _ownsProvider = ownsProvider;
        Info = new CompositionProviderSnapshotInfo
        {
            Name = ValidateName(name),
            Boundary = boundary,
            CreatedAt = DateTimeOffset.UtcNow,
            OwnsProvider = ownsProvider,
            ServiceCount = serviceCount
        };
    }

    public CompositionProviderSnapshotInfo Info { get; }

    public string Name => Info.Name;

    public CompositionProviderBoundary Boundary => Info.Boundary;

    public DateTimeOffset CreatedAt => Info.CreatedAt;

    public bool OwnsProvider => Info.OwnsProvider;

    public int? ServiceCount => Info.ServiceCount;

    public IServiceProvider Services => this;

    public static CompositionServiceProviderSnapshot CreateExternalHost(
        string name,
        IServiceProvider provider)
        => new(
            name,
            CompositionProviderBoundary.Host,
            provider,
            ownsProvider: false,
            serviceCount: null);

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return GetProvider().GetService(serviceType);
    }

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return GetKeyedProvider().GetKeyedService(serviceType, serviceKey);
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return GetKeyedProvider().GetRequiredKeyedService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        var provider = Interlocked.Exchange(ref _provider, null);
        if (!_ownsProvider || provider is null)
            return;

        if (provider is IDisposable disposable)
            disposable.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        var provider = Interlocked.Exchange(ref _provider, null);
        if (!_ownsProvider || provider is null)
            return;

        if (provider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (provider is IDisposable disposable)
            disposable.Dispose();
    }

    private IServiceProvider GetProvider()
        => Volatile.Read(ref _provider)
           ?? throw new ObjectDisposedException(nameof(CompositionServiceProviderSnapshot));

    private IKeyedServiceProvider GetKeyedProvider()
        => GetProvider() as IKeyedServiceProvider
           ?? throw new InvalidOperationException(
               "The underlying provider does not support keyed services.");

    internal static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Snapshot name cannot have surrounding whitespace.", nameof(name));
        return name;
    }
}
