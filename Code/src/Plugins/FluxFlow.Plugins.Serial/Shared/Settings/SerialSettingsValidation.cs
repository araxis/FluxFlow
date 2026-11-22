using FluxFlow.Components.Serial;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Authoring.Definitions.Serial;

public static class SerialSettingsValidation
{
    // Validate stored settings without resolving a session or opening a physical port.
    public static void Validate(IConfiguration component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component["Connection"]);
        if (component["Type"] == "serial.text")
            component.Get<SerialTextOptions>()!.Validate();
        else
            component.Get<SerialFrameOptions>()!.Validate();
    }
}
