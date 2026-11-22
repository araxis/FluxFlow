using System.Text.Json;
using FluxFlow.Authoring.Definitions.Serial;
using FluxFlow.Environments;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.Serial;

public sealed class SerialDesign : IPluginDesign
{
    public PropertyRules Component(PluginComponent component) => component.Editor == "properties"
        ? new() { Validate = SerialSettingsValidation.Validate, FieldErrors = SerialFrameValidation.Fields }
        : new() { Validate = SerialSettingsValidation.Validate };

    public PropertyRules Connection(PluginConnection connection) => new()
    {
        Validate = settings => SerialConnectionValidation.Validate(settings, connection),
        IsVisible = FlowFieldVisible
    };

    public void ConfigureRemoteServices(IServiceCollection services) =>
        services.AddSingleton<IResourceEnvironmentAdapter, SerialEnvironmentAdapter>();

    private static JsonElement Field(IReadOnlyDictionary<string, JsonElement> settings, string name) =>
        settings.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool FlowFieldVisible(IReadOnlyDictionary<string, JsonElement> settings, string name)
    {
        if (name != "RtsEnable" || Field(settings, "Handshake").ToString().ToUpperInvariant() is not ("REQUESTTOSEND" or "REQUESTTOSENDXONXOFF" or "2" or "3")) return true;
        var saved = Field(settings, name);
        return saved.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) &&
            (!bool.TryParse(saved.ToString(), out var enabled) || enabled);
    }
}
