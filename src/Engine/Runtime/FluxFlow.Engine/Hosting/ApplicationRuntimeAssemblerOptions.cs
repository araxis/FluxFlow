using FluxFlow.Engine.Ports;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimeAssemblerOptions
{
    public Events.ApplicationEventOptions Events { get; set; } = new();

    public int InputCapacity { get; set; } = ApplicationPortRuntimeBuilder.DefaultInputCapacity;

    public int OutputCapacity { get; set; } = ApplicationPortRuntimeBuilder.DefaultOutputCapacity;
}
