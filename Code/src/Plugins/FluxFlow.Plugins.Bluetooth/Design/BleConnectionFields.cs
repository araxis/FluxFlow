using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Environments;

namespace FluxFlow.Plugins.Bluetooth.Design;

internal static class BleConnectionFields
{
    private const string DefaultAdapter = "ble-adapter://default";
    public static PropertyRules Describe(bool adapter) => new()
    {
        Validate = config => Validate(adapter, config["Endpoint"]),
        Preview = settings =>
        {
            var endpoint = ResourceJson.Text(settings, "Endpoint");
            Validate(adapter, endpoint);
            return new(endpoint, []);
        },
        Groups = _ => [new("Target", null, []) { Fields = settings => Fields(adapter, settings) }]
    };

    private static void Validate(bool adapter, string? endpoint)
    {
        var connection = new BleConnection(endpoint ?? "");
        if (adapter) connection.ValidateAdapter(); else connection.Apply(new());
    }

    private static string? Error(Action validate)
    {
        try { validate(); return null; }
        catch (ArgumentException error) { return error.Message; }
    }

    private static IReadOnlyList<ProjectedField> Fields(bool adapter, IReadOnlyDictionary<string, JsonElement> settings)
    {
        var key = settings.Keys.FirstOrDefault(k => k.Equals("Endpoint", StringComparison.OrdinalIgnoreCase)) ?? "Endpoint";
        var saved = settings.GetValueOrDefault(key);
        var endpoint = saved.ValueKind switch
        {
            JsonValueKind.String => saved.GetString()!,
            JsonValueKind.Undefined => "",
            _ => saved.GetRawText()
        };
        void Save(IDictionary<string, JsonElement> values, string value) => values[key] = JsonSerializer.SerializeToElement(value);
        string? Check(string? value) => Error(() => Validate(adapter, value));
        var diagnostic = Check(endpoint);
        var display = new ProjectedField("Endpoint", endpoint);
        if (adapter)
        {
            var choices = new List<OptionChoiceModel> { Choice(diagnostic is null ? endpoint : DefaultAdapter, "Default") };
            if (diagnostic is not null) choices.Add(Choice(endpoint, "Unsupported saved endpoint"));
            return [new("Adapter", endpoint) { Choices = choices, Write = Save, Validate = Check, Diagnostic = diagnostic,
                Hint = "Only the execution host's default adapter is supported." }, display];
        }
        if (diagnostic is not null)
            return saved.ValueKind is JsonValueKind.String or JsonValueKind.Null or JsonValueKind.Undefined
                ? [display with { Validate = Check, Write = Save }]
                : [display with { Value = "Use Advanced JSON to edit this saved value." }];
        var target = new BleConnection(endpoint).Apply(new());
        static string Compact(string value) => value.Replace(":", "").Replace("-", "");
        return [
            new("Device address", target.DeviceAddress)
            {
                Validate = value => Error(() => new BleClientOptions { DeviceAddress = value ?? "" }.ValidateDevice()),
                Write = (values, value) =>
                {
                    if (!Compact(value).Equals(Compact(target.DeviceAddress), StringComparison.OrdinalIgnoreCase))
                        Save(values, $"ble://{Compact(value)}/{target.AddressType.ToString().ToLowerInvariant()}");
                }
            },
            new("Address type", target.AddressType.ToString())
            {
                Choices = Enum.GetNames<BleAddressType>().Select(name => Choice(name, name)).ToArray(),
                Write = (values, value) => Save(values, $"ble://{target.DeviceAddress}/{value.ToLowerInvariant()}")
            },
            display];
    }

    private static OptionChoiceModel Choice(string value, string title) => new() { Value = value, DisplayName = title };
}
