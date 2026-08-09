using FluxFlow.Composition.Addressing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Internal.Snapshots;

internal sealed class CompositionServiceProviderSnapshotBuilder
{
    private readonly object _gate = new();
    private readonly List<ServiceDescriptor> _descriptors = [];

    public int ServiceCount
    {
        get
        {
            lock (_gate)
                return _descriptors.Count;
        }
    }

    public CompositionServiceProviderSnapshotBuilder AddServices(
        IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        lock (_gate)
            _descriptors.AddRange(services);
        return this;
    }

    public CompositionServiceProviderSnapshotBuilder ConfigureServices(
        Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var services = new ServiceCollection();
        configure(services);
        return AddServices(services);
    }

    public CompositionServiceProviderSnapshotBuilder BridgeExternalSingleton<TService>(
        TService service)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(service);
        var services = new ServiceCollection();
        services.AddSingleton(service);
        return AddServices(services);
    }

    public CompositionServiceProviderSnapshotBuilder BridgeExternalKeyedSingleton<TService>(
        object serviceKey,
        TService service)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(serviceKey);
        ArgumentNullException.ThrowIfNull(service);
        var services = new ServiceCollection();
        services.AddKeyedSingleton(serviceKey, service);
        return AddServices(services);
    }

    public CompositionServiceProviderSnapshotBuilder BridgeExternalResource<TService>(
        ApplicationAddress address,
        TService service)
        where TService : class
    {
        ValidateAddress(address, ApplicationAddressKind.Resource);
        return BridgeExternalKeyedSingleton(address.Value, service);
    }

    public CompositionServiceProviderSnapshot Build(
        CompositionProviderBoundary boundary,
        string name,
        ServiceProviderOptions? options = null,
        IServiceProvider? fallbackProvider = null)
    {
        if (!Enum.IsDefined(boundary))
            throw new ArgumentOutOfRangeException(nameof(boundary), boundary, "Unknown provider boundary.");
        CompositionServiceProviderSnapshot.ValidateName(name);

        ServiceDescriptor[] descriptors;
        lock (_gate)
            descriptors = _descriptors.ToArray();

        var services = new ServiceCollection();
        foreach (var descriptor in descriptors)
            ((IServiceCollection)services).Add(descriptor);

        var provider = services.BuildServiceProvider(options ?? new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        return new CompositionServiceProviderSnapshot(
            name,
            boundary,
            provider,
            ownsProvider: true,
            descriptors.Length,
            fallbackProvider);
    }

    private static void ValidateAddress(
        ApplicationAddress address,
        ApplicationAddressKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Kind != expectedKind)
        {
            throw new ArgumentException(
                $"Address '{address}' must be a {expectedKind} address.",
                nameof(address));
        }
    }
}
