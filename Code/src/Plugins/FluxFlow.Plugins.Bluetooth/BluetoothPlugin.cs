using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.BleClient;
using FluxFlow.Authoring.Definitions.BleNotifications;
using FluxFlow.Authoring.Definitions.BleServer;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Configuration.Components.Bluetooth;
using FluxFlow.Execution.Plugins;
using FluxFlow.Plugins.Bluetooth.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Plugins.Bluetooth;

public sealed class BluetoothPlugin : IFluxFlowPlugin
{
    public PluginDescription Description { get; } = new("FluxFlow.Plugins.Bluetooth",
        [Describe(new BleClientDescription()), Describe(new BleNotificationsDescription()), Describe(new BleServerDescription())],
        [BluetoothDesign.Peripheral, BluetoothDesign.Adapter])
    {
        Design = new(typeof(BluetoothDesign).Assembly.GetName().Name!, typeof(BluetoothDesign).FullName!,
            [typeof(BleClientOptions).Assembly.GetName().Name!])
    };

    private static PluginComponent Describe(IComponentDescription description) =>
        new(description.Palette, description.Inspector, description.Documentation) { Editor = "properties" };

    public void ConfigureWorkflow(IServiceCollection services) => services.AddBluetooth();

    public void ConfigureHost(IServiceCollection services)
    {
        // Only Windows-targeted companions opt into native Bluetooth. Loading or browsing a project never opens the radio.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
            && Assembly.GetEntryAssembly()?.GetCustomAttribute<TargetPlatformAttribute>()?.PlatformName.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) == true)
        {
            var context = AssemblyLoadContext.GetLoadContext(typeof(BluetoothPlugin).Assembly)!;
            var native = context.LoadFromAssemblyName(new AssemblyName("FluxFlow.Components.Bluetooth.Windows"))
                .GetType("FluxFlow.Components.Bluetooth.Windows.WindowsBlePlatform", throwOnError: true)!;
            services.TryAddSingleton(native);
            services.TryAddSingleton<IBlePlatform>(provider => (IBlePlatform)provider.GetRequiredService(native));
            services.TryAddSingleton<IBleExplorer>(provider => (IBleExplorer)provider.GetRequiredService(native));
        }
        BleInfrastructure.Register(services);
        services.TryAddSingleton<IBlePlatform, UnavailableBlePlatform>();
        services.TryAddSingleton<IBleExplorer, UnavailableBleExplorer>();
        services.AddSingleton<IWorkflowHostSupport, BleHostSupport>();
        services.AddSingleton<IPluginEndpoints, BleExplorerEndpoints>();
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, BleExplorerJsonContext.Default));
    }
}
