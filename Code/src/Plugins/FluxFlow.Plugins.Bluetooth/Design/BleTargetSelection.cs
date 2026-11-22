using System.Text.Json;
using FluxFlow.Components.Bluetooth;

namespace FluxFlow.Plugins.Bluetooth.Design;

public sealed record BleTargetSelection(string Connection, string DeviceAddress, BleAddressType AddressType,
    Guid? ServiceId = null, Guid? CharacteristicId = null)
{
    public void Apply(IDictionary<string, JsonElement> configuration)
    {
        if (BleClientProperties.Text(configuration, "Connection") != Connection)
            BleClientProperties.Set(configuration, "Connection", Connection);
        if (string.IsNullOrWhiteSpace(Connection))
        {
            if (!string.Equals(BleClientProperties.Text(configuration, "DeviceAddress"), DeviceAddress, StringComparison.OrdinalIgnoreCase))
                BleClientProperties.Set(configuration, "DeviceAddress", DeviceAddress);
            var hasType = configuration.Keys.Any(k => k.Equals("AddressType", StringComparison.OrdinalIgnoreCase));
            if ((hasType ? BleClientProperties.EnumValue<BleAddressType>(configuration, "AddressType") : BleAddressType.Public) != AddressType)
                BleClientProperties.Set(configuration, "AddressType", AddressType.ToString());
        }
        if (ServiceId is { } service && CharacteristicId is { } characteristic)
        {
            SetUuid("ServiceId", service);
            SetUuid("CharacteristicId", characteristic);
        }
        void SetUuid(string name, Guid id)
        {
            if (!Guid.TryParse(BleClientProperties.Text(configuration, name), out var saved) || saved != id)
                BleClientProperties.Set(configuration, name, id.ToString());
        }
    }
}
