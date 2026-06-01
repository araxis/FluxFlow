using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Storage;

public static class StorageComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterStorageComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterStorageComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterStorageComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<StorageComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new StorageComponentOptions();
        configure(options);
        return registry.Register(new StorageComponentModule(options));
    }
}
