using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.SerialReader;
using FluxFlow.Authoring.Definitions.SerialText;
using FluxFlow.Authoring.Definitions.SerialWriter;
using FluxFlow.Configuration.Components.Serial;
using FluxFlow.Environments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Plugins.Serial;

public sealed class SerialPlugin : IFluxFlowPlugin
{
    public PluginDescription Description { get; } = new("FluxFlow.Plugins.Serial",
        [Component(new SerialTextDescription()), Component(new SerialReaderDescription(), "properties"), Component(new SerialWriterDescription(), "properties")],
        [SerialPortSchema.Describe()]) {
        Design = new(typeof(SerialDesign).Assembly.GetName().Name!, typeof(SerialDesign).FullName!,
            [typeof(FluxFlow.Components.Serial.SerialFrameOptions).Assembly.GetName().Name!])
    };

    private static PluginComponent Component(IComponentDescription description, string? editor = null) =>
        new(description.Palette, description.Inspector, description.Documentation, editor);

    public void ConfigureWorkflow(IServiceCollection services) => services.AddSerial();
    public void ConfigureHost(IServiceCollection services)
    {
        services.AddSingleton<FluxFlow.Execution.Plugins.IPluginEndpoints, FluxFlow.Execution.Serial.SerialDiscoveryEndpoints>();
        services.AddSingleton<IResourceEnvironmentAdapter, SerialEnvironmentAdapter>();
        services.AddKeyedSingleton<Func<CancellationToken, Task<string[]>>>(new SuggestionKey(Description.Id, "ports"),
            (provider, _) => provider.GetRequiredService<ISerialPortDiscovery>().ListAsync);
        services.TryAddSingleton<ISerialPortDiscovery, FluxFlow.Execution.Serial.NativeSerialPortDiscovery>();
    }
}
