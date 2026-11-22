using System.Text.Json;
using FluxFlow.Components.Bluetooth;

namespace FluxFlow.Plugins.Bluetooth.Design;

public static class BleClientProperties
{
    public static bool InputOperation(IDictionary<string, JsonElement> configuration)
    {
        var input = configuration.FirstOrDefault(pair => pair.Key.Equals("UseInputOperation", StringComparison.OrdinalIgnoreCase)).Value;
        return input.ValueKind == JsonValueKind.True || input.ValueKind == JsonValueKind.String && bool.TryParse(input.GetString(), out var enabled) && enabled;
    }

    public static void Set<T>(IDictionary<string, JsonElement> configuration, string name, T value)
    {
        var key = configuration.Keys.FirstOrDefault(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
        if (value is string text && string.IsNullOrWhiteSpace(text)) configuration.Remove(key);
        else configuration[key] = JsonSerializer.SerializeToElement(value);
    }

    private static JsonElement Field(IDictionary<string, JsonElement> configuration, string name) =>
        configuration.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    public static string Text(IDictionary<string, JsonElement> configuration, string name) =>
        Field(configuration, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";

    public static T? EnumValue<T>(IDictionary<string, JsonElement> configuration, string name) where T : struct, Enum =>
        Field(configuration, name) is { ValueKind: JsonValueKind.Number or JsonValueKind.String } field &&
        Enum.TryParse<T>(field.ToString(), true, out var value) && Enum.IsDefined(value) ? value : null;

    public static bool IsVisible(IDictionary<string, JsonElement> configuration, string name)
    {
        if (name is "DeviceAddress" or "AddressType") return string.IsNullOrWhiteSpace(Text(configuration, "Connection"));
        if (Text(configuration, "Type") == "ble.notifications") return true;
        var dynamicOperation = InputOperation(configuration);
        if (name is "Operation" or "ServiceId" or "CharacteristicId") return !dynamicOperation;
        if (name is "WriteMode" or "Value") return !dynamicOperation && EnumValue<BleOperationKind>(configuration, "Operation") == BleOperationKind.Write;
        return true;
    }

    public static BlePayload? Payload(IDictionary<string, JsonElement> configuration)
    {
        var value = Field(configuration, "Value");
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return new();
        if (value.ValueKind != JsonValueKind.Object) return null;
        var fields = value.Deserialize<Dictionary<string, JsonElement>>()!;
        var encoding = Field(fields, "Encoding").ValueKind == JsonValueKind.Undefined
            ? BleValueEncoding.Hex : EnumValue<BleValueEncoding>(fields, "Encoding");
        if (encoding is null || Field(fields, "Data").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.String)) return null;
        return new() { Encoding = encoding.Value, Data = Text(fields, "Data") };
    }

    public static BleClientOptions PreviewOptions(IDictionary<string, JsonElement> configuration)
    {
        var options = new BleClientOptions
        {
            DeviceAddress = Text(configuration, "DeviceAddress"),
            ServiceId = Guid.TryParse(Text(configuration, "ServiceId"), out var service) ? service : Guid.Empty,
            CharacteristicId = Guid.TryParse(Text(configuration, "CharacteristicId"), out var characteristic) ? characteristic : Guid.Empty,
        };
        if (Field(configuration, "AddressType").ValueKind != JsonValueKind.Undefined)
            options = options with { AddressType = EnumValue<BleAddressType>(configuration, "AddressType")
                ?? throw new ArgumentException("Address type must be Public or Random.") };
        if (Field(configuration, "TimeoutSeconds") is { ValueKind: not JsonValueKind.Undefined } timeout)
            options = options with { TimeoutSeconds = int.TryParse(timeout.ToString(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : throw new ArgumentException("Enter a whole number of seconds.") };
        options.Validate();
        return options;
    }

    public static void SetPayload(IDictionary<string, JsonElement> configuration, BlePayload payload)
    {
        var value = Field(configuration, "Value");
        var fields = value.ValueKind == JsonValueKind.Object ? value.Deserialize<Dictionary<string, JsonElement>>()! : new();
        if (payload.Encoding != (EnumValue<BleValueEncoding>(fields, "Encoding") ?? BleValueEncoding.Hex))
            Set(fields, "Encoding", payload.Encoding.ToString());
        var key = fields.Keys.FirstOrDefault(k => k.Equals("Data", StringComparison.OrdinalIgnoreCase)) ?? "Data";
        fields[key] = JsonSerializer.SerializeToElement(payload.Data);
        Set(configuration, "Value", fields);
    }
}
