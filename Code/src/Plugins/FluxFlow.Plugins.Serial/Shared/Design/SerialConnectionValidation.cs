using Microsoft.Extensions.Configuration;

namespace FluxFlow.Plugins.Serial;

internal static class SerialConnectionValidation
{
    internal static void Validate(IConfiguration settings, FluxFlow.Plugins.PluginConnection schema)
    {
        foreach (var field in schema.Sections.SelectMany(s => s.Options).Where(f => f.Choices.Count > 0))
            if (settings[field.Name] is { } text && !field.Choices.Any(c => c.Value.Equals(text, StringComparison.OrdinalIgnoreCase) || c.NumericValue == text))
                throw new ArgumentException($"Invalid {field.DisplayName}.");
        _ = settings.GetValue("DtrEnable", false);
        FluxFlow.Configuration.Components.Serial.SerialPortLimits.Validate(settings["PortName"], settings.GetValue("BaudRate", 115200),
            settings.GetValue("DataBits", 8), settings.GetValue("ReadTimeout", TimeSpan.FromMilliseconds(250)),
            settings.GetValue("WriteTimeout", TimeSpan.FromSeconds(2)), settings.GetValue("RtsEnable", false),
            settings["Handshake"]?.ToUpperInvariant() is "REQUESTTOSEND" or "REQUESTTOSENDXONXOFF" or "2" or "3");
    }
}
