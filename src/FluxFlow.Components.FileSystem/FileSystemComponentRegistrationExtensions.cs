using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.FileSystem;

public static class FileSystemComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterFileSystemComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterFileSystemComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterFileSystemComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<FileSystemComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new FileSystemComponentOptions();
        configure(options);
        return registry.Register(new FileSystemComponentModule(options));
    }
}
