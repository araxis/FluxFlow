using FluxFlow.Plugins.Hierarchies;
using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Components.Bluetooth;
using static FluxFlow.Plugins.Bluetooth.Design.BleServerDraft;

namespace FluxFlow.Plugins.Bluetooth.Design;

public sealed class BleGattDocument : IHierarchyDocument
{
    public static HierarchyEditor Editor { get; } = new("BLE GATT server", "GATT services", "Edit services",
        "Define primary services, characteristic values and access. Adapter and advertising settings are in the Inspector.", () => new BleGattDocument());
    private BleServerDraft _draft = null!;
    private Service? _service;
    private Characteristic? _item;
    public void Read(string json)
    {
        var draft = BleServerDraft.FromJson(json);
        var service = draft.Services.FirstOrDefault(s => s.Id == _service?.Id) ?? draft.Services.FirstOrDefault();
        Select(service, service?.Characteristics.FirstOrDefault(c => c.Id == _item?.Id));
        _draft = draft;
    }
    public string ToJson() => _draft.ToJson();
    public string Save() => JsonSerializer.Serialize(_draft.Save());
    private void Select(Service? service, Characteristic? item = null) => (_service, _item) = (service, item);

    public IEnumerable<DocumentNode> Nodes
    {
        get
        {
            foreach (var service in _draft.Services)
            {
                yield return new(service, 0, string.IsNullOrWhiteSpace(service.Name) ? "Primary service" : service.Name,
                    service.Id, _service == service && _item is null, () => Select(service));
                foreach (var item in service.Characteristics)
                {
                    var name = BleStandards.Entries.FirstOrDefault(e => e.Kind == BleUuidKind.Characteristic && string.Equals(e.Id.ToString(), item.Id, StringComparison.OrdinalIgnoreCase))?.Name;
                    yield return new(item, 1, name ?? "Characteristic", item.Id, _item == item, () => Select(service, item));
                }
            }
        }
    }

    public IEnumerable<DocumentAction> Actions
    {
        get
        {
            var room = _draft.Services.Count < 8;
            yield return new("Add service", () => Select(_draft.AddService()), room);
            yield return new("Battery Level · Read", () => Select(_draft.AddService(BleStandards.BatteryTemplate(false))), room, "Service templates");
            yield return new("Battery Level · Read and notify", () => Select(_draft.AddService(BleStandards.BatteryTemplate(true))), room, "Service templates");
            yield return new("Tx Power · Read", () => Select(_draft.AddService(BleStandards.TxPowerTemplate())), room, "Service templates");
        }
    }

    public IEnumerable<DocumentField> Fields => _item is { } item && _service is { } parent ? CharacteristicFields(parent, item) :
        _service is { } service ? ServiceFields(service) : [new DocumentNote("Add a primary service or choose a template.")];

    private IEnumerable<DocumentField> ServiceFields(Service service)
    {
        yield return new DocumentTitle("Primary service", new("Remove service", () => { _draft.RemoveService(service); Select(_draft.Services.FirstOrDefault()); }));
        yield return new DocumentText("Name", service.Name, value => service.Name = value);
        yield return Uuid("Service UUID", service.Id, value => service.Id = value, BleUuidKind.Service);
        yield return new DocumentNote("Primary services are published together. Windows reserves GAP, GATT, Device Information and Scan Parameters.");
        if (service.Id == BleStandards.Uuid(0x1804).ToString())
            yield return new DocumentNote("The template starts at 0 dBm as an example. Set the characteristic to this device's actual transmit power; Studio does not measure or change radio power.");
        yield return new DocumentAction("Add characteristic", () => Select(service, service.Add()), service.Characteristics.Count < 32);
    }

    private IEnumerable<DocumentField> CharacteristicFields(Service parent, Characteristic item)
    {
        yield return new DocumentTitle("Characteristic", new("Remove characteristic", () => {
            var index = parent.Characteristics.IndexOf(item);
            parent.Characteristics.RemoveAt(index);
            Select(parent, parent.Characteristics.Count == 0 ? null : parent.Characteristics[Math.Min(index, parent.Characteristics.Count - 1)]);
        }));
        yield return Uuid("Characteristic UUID", item.Id, value => item.Id = value, BleUuidKind.Characteristic);
        foreach (var flag in new[] { BleCharacteristicProperties.Read, BleCharacteristicProperties.Write, BleCharacteristicProperties.WriteWithoutResponse, BleCharacteristicProperties.Notify, BleCharacteristicProperties.Indicate })
            yield return new DocumentFlag(flag.ToString(), item.Properties.HasFlag(flag), value => item.Properties = value ? item.Properties | flag : item.Properties & ~flag);
        yield return Protection("Read protection", item.ReadProtection, p => item.ReadProtection = p);
        yield return Protection("Write protection", item.WriteProtection, p => item.WriteProtection = p);
        yield return new DocumentBinary(BleValueFormat.ToEditor(item.Payload), v => item.Payload = BleValueFormat.FromEditor(v), BleValueFormat.For(item.Id));
        yield return new DocumentText("User description (0x2901)", item.UserDescription, v => item.UserDescription = v);
        yield return new DocumentNote("Windows generates the client configuration descriptor for Notify/Indicate. Custom descriptors below are read-only.");
        foreach (var descriptor in item.Descriptors)
            yield return new DocumentGroup(descriptor, $"Custom descriptor · {descriptor.Id}", [
                Uuid("Descriptor UUID", descriptor.Id, v => descriptor.Id = v, BleUuidKind.Descriptor),
                Protection("Descriptor read protection", descriptor.ReadProtection, p => descriptor.ReadProtection = p),
                new DocumentBinary(BleValueFormat.ToEditor(descriptor.Payload), v => descriptor.Payload = BleValueFormat.FromEditor(v), BleValueFormat.For()),
                new DocumentAction("Remove descriptor", () => item.Descriptors.Remove(descriptor))]);
        yield return new DocumentAction("Add custom descriptor", () => item.AddDescriptor(), item.Descriptors.Count < 16);
    }
    private static DocumentText Uuid(string label, string value, Action<string> change, BleUuidKind kind) =>
        new(label, value, change, new("Search standard UUIDs", search => BleStandards.Search(search, kind).Select(entry =>
            new TextSuggestion(entry.Id.ToString(), entry.Label, entry.Support, kind != BleUuidKind.Descriptor && (kind != BleUuidKind.Service || !BleStandards.IsSystemService(entry.Id)))),
            "Generate custom UUID", () => BleStandards.NewCustomUuid().ToString()));

    private static readonly OptionChoiceModel[] Protections = [
        OptionChoiceModel.FromEnum(BleAccessProtection.Plain, "No additional protection"),
        OptionChoiceModel.FromEnum(BleAccessProtection.AuthenticationRequired, "Authenticated"),
        OptionChoiceModel.FromEnum(BleAccessProtection.EncryptionRequired, "Encrypted"),
        OptionChoiceModel.FromEnum(BleAccessProtection.EncryptionAndAuthenticationRequired, "Encrypted and authenticated")];
    private static DocumentChoice Protection(string label, BleAccessProtection value, Action<BleAccessProtection> change) =>
        new(label, value.ToString(), v => change(Enum.Parse<BleAccessProtection>(v)), Protections);
}
