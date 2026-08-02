using FluxFlow.Engine.Ports;

namespace FluxFlow.Engine;

public sealed class FluxFlowApplicationOptions
{
    public string InitialRevisionId { get; set; } = "initial";

    public bool StartWithHost { get; set; } = true;

    public bool StopWithHost { get; set; } = true;

    public int InputCapacity { get; set; } = ApplicationPortRuntimeBuilder.DefaultInputCapacity;

    public int OutputCapacity { get; set; } = ApplicationPortRuntimeBuilder.DefaultOutputCapacity;
}
