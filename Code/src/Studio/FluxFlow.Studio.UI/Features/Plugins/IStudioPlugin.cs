using FluxFlow.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Studio.UI.Features.Plugins;

public interface IStudioPlugin
{
    void Configure(IServiceCollection services, PluginDescription description, bool companionInProcess = false);
}

public sealed record PluginSettingsValidator(string Type, PropertyRules Rules);

public static class StudioPluginLoader
{
    public static void LoadDependency(string name, Stream stream) => ReadAssembly(name, stream);

    public static void LoadClientAssembly(IServiceCollection services, PluginDescription plugin, string name, Stream stream, bool inProcess)
    {
        var assembly = ReadAssembly(name, stream);
        if (plugin.Design is not { } entry || name != entry.Assembly) return;
        if (Activator.CreateInstance(assembly.GetType(entry.Type, throwOnError: true)!) is not IPluginDesign design)
            throw new InvalidDataException($"Invalid property definitions in {plugin.Id}.");
        PortableProperties.Configure(services, plugin, design, inProcess);
    }

    private static System.Reflection.Assembly ReadAssembly(string name, Stream stream)
    {
        var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(stream);
        if (assembly.GetName().Name != name)
            throw new InvalidDataException($"Unexpected Studio dependency: expected {name}.");
        return assembly;
    }

    public static void Configure(IServiceCollection services, PluginDescription plugin, Stream stream, bool companionInProcess = false)
    {
        var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(stream);
        if (assembly.GetName().Name != plugin.Id + ".Studio" ||
            Activator.CreateInstance(assembly.GetType(plugin.StudioEntry!, throwOnError: true)!) is not IStudioPlugin module)
            throw new InvalidDataException($"Invalid Studio editor in {plugin.Id}.");
        module.Configure(services, plugin, companionInProcess);
    }
}
