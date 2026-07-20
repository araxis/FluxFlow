using FluxFlow.Engine.Ports;

namespace FluxFlow.Engine.Hosting;

/// <summary>Provides host access to the address-stable application ports.</summary>
public interface IApplicationRuntimeAccess
{
    ApplicationPortRuntime? Ports { get; }

    ApplicationPortRuntime GetRequiredPorts();
}
