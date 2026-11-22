using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Components.Bluetooth;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Plugins.Bluetooth.Design;

// Detached JSON retains unknown fields and casing until the user explicitly changes them.
public sealed class BleServerDraft
{
    private readonly JsonObject _value;
    private readonly Service? _legacyService;
    public BleServerDraft(IDictionary<string, JsonElement> configuration)
    {
        _value = new(configuration.Select(pair => KeyValuePair.Create(pair.Key, JsonNode.Parse(pair.Value.GetRawText()))));
        var multiple = Get(_value, "Services") is not null;
        if (multiple && (Get(_value, "ServiceId") is not null || Get(_value, "Characteristics") is not null))
            throw new ArgumentException("Use Services or the legacy ServiceId/Characteristics fields, not both.");
        if (multiple)
            foreach (var item in Objects(_value, "Services")) Services.Add(new(item));
        else
        {
            _legacyService = new(new JsonObject { ["Id"] = Text(_value, "ServiceId"), ["Characteristics"] = Get(_value, "Characteristics")?.DeepClone() });
            Services.Add(_legacyService);
        }
        _ = IsConnectable; _ = IsDiscoverable;
        foreach (var service in Services)
        {
            _ = service.Id; _ = service.Name;
            foreach (var item in service.Characteristics)
            {
                _ = item.Id; _ = item.Value; _ = item.Payload; _ = item.Properties; _ = item.ReadProtection; _ = item.WriteProtection; _ = item.UserDescription;
                foreach (var descriptor in item.Descriptors) { _ = descriptor.Id; _ = descriptor.Payload; _ = descriptor.ReadProtection; }
            }
        }
    }
    public List<Service> Services { get; } = [];
    public string Adapter { get => Text(_value, "Adapter"); set { if (string.IsNullOrWhiteSpace(value)) _value.Remove(Key(_value, "Adapter")); else Set(_value, "Adapter", value.Trim()); } }
    public string ServiceId { get => Services[0].Id; set => Services[0].Id = value; }
    public List<Characteristic> Characteristics => Services[0].Characteristics;
    public bool IsConnectable { get => Scalar(_value, "IsConnectable", true); set => Set(_value, "IsConnectable", value); }
    public bool IsDiscoverable { get => Scalar(_value, "IsDiscoverable", true); set => Set(_value, "IsDiscoverable", value); }
    public Service AddService(BleService? template = null)
    {
        if (Services.Count >= 8) throw new InvalidOperationException("At most 8 primary services are supported.");
        var data = new JsonObject { ["Id"] = (template?.Id ?? BleStandards.NewCustomUuid()).ToString(), ["Name"] = template?.Name ?? "Custom service" };
        var service = new Service(data);
        if (template is not null)
            foreach (var item in template.Characteristics)
            {
                var added = service.Add(); added.Id = item.Id.ToString(); added.Properties = item.Properties;
                added.Payload = item.InitialValue ?? new() { Encoding = BleValueEncoding.Utf8, Data = item.Value };
            }
        Services.Add(service);
        return service;
    }
    public void RemoveService(Service service) => Services.Remove(service);
    public Characteristic Add() => Services[0].Add();
    public string ToJson() => Serialize().ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    private JsonObject Serialize()
    {
        var result = (JsonObject)_value.DeepClone();
        if (Services.Count != 1 || Services[0] != _legacyService || Services[0].Name.Length > 0)
        {
            result.Remove(Key(result, "ServiceId"));
            result.Remove(Key(result, "Characteristics"));
            result[Key(result, "Services")] = new JsonArray(Services.Select(service => (JsonNode)service.Serialize()).ToArray());
        }
        else
        {
            Set(result, "ServiceId", Services[0].Id);
            var service = Services[0].Serialize();
            result[Key(result, "Characteristics")] = Get(service, "Characteristics")!.DeepClone();
        }
        return result;
    }
    public static BleServerDraft FromJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("The GATT configuration must be a JSON object.");
        ValidateKeys(text);
        var draft = new BleServerDraft(document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone()));
        if (Text(draft._value, "Type") != "ble.server") throw new ArgumentException("Keep the Type property set to ble.server.");
        return draft;
    }
    private static void ValidateKeys(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"Server\":" + text + "}"));
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
    }
    public Dictionary<string, JsonElement> Save()
    {
        var value = Serialize();
        ValidateKeys(value.ToJsonString());
        new BleServerOptions { Services = Services.Select(service => service.ToOptions()).ToArray(), IsConnectable = IsConnectable,
            IsDiscoverable = IsDiscoverable, OutputCapacity = Scalar(_value, "OutputCapacity", 128) }.Validate();
        return value.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value), StringComparer.Ordinal);
    }
    private static IEnumerable<JsonObject> Objects(JsonObject value, string key)
    {
        if (Get(value, key) is null) return [];
        if (Get(value, key) is not JsonArray array) throw new ArgumentException($"{key} must be a JSON array.");
        return array.Select(item => item as JsonObject ?? throw new ArgumentException($"Each {key} entry must be an object."));
    }
    private static string Key(JsonObject value, string key) => value.Select(pair => pair.Key).FirstOrDefault(name => name.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key;
    private static JsonNode? Get(JsonObject value, string key) => value[Key(value, key)];
    private static string Text(JsonObject value, string key) => Get(value, key)?.GetValue<string>() ?? "";
    private static void Set<T>(JsonObject value, string key, T field) => value[Key(value, key)] = JsonSerializer.SerializeToNode(field);
    private static T Scalar<T>(JsonObject value, string key, T fallback) where T : IParsable<T>
    {
        var node = Get(value, key);
        if (node is null) return fallback;
        return node is JsonValue && T.TryParse(node.ToString(), CultureInfo.InvariantCulture, out var parsed) ? parsed : throw new ArgumentException($"Invalid {key}.");
    }
    private static T Choice<T>(JsonObject data, string key, T fallback = default) where T : struct, Enum
    {
        if (Get(data, key) is null) return fallback;
        return Get(data, key) is JsonValue node && Enum.TryParse<T>(node.ToString(), true, out var parsed)
            ? parsed : throw new ArgumentException($"Invalid {key}.");
    }
    public sealed class Service(JsonObject data)
    {
        internal BleService ToOptions() => new() { Id = Guid.Parse(Id), Name = Name, Characteristics = Characteristics.Select(item => item.ToOptions()).ToArray() };
        public string Id { get => Text(data, "Id"); set => Set(data, "Id", value); }
        public string Name { get => Text(data, "Name"); set => Set(data, "Name", value); }
        public List<Characteristic> Characteristics { get; } = Objects(data, "Characteristics").Select(item => new Characteristic(item)).ToList();
        public Characteristic Add()
        {
            if (Characteristics.Count >= 32) throw new InvalidOperationException("At most 32 characteristics per service are supported.");
            var item = new Characteristic(new() { ["Id"] = BleStandards.NewCustomUuid().ToString(), ["Value"] = "" });
            Characteristics.Add(item); return item;
        }
        internal JsonObject Serialize()
        {
            var result = (JsonObject)data.DeepClone();
            result[Key(result, "Characteristics")] = new JsonArray(Characteristics.Select(item => (JsonNode)item.Serialize()).ToArray());
            return result;
        }
    }
    public sealed class Characteristic(JsonObject data)
    {
        internal BleCharacteristic ToOptions() => new() { Id = Guid.Parse(Id), InitialValue = Payload, Properties = Properties,
            ReadProtection = ReadProtection, WriteProtection = WriteProtection, UserDescription = UserDescription,
            Descriptors = Descriptors.Select(item => new BleDescriptor { Id = Guid.Parse(item.Id), Value = item.Payload, ReadProtection = item.ReadProtection }).ToArray() };
        public string Id { get => Text(data, "Id"); set => Set(data, "Id", value); }
        public string Value { get => Text(data, "Value"); set => Set(data, "Value", value); }
        public int ByteCount => Get(data, "InitialValue") is null ? Encoding.UTF8.GetByteCount(Value) : Payload.Decode().Length;
        public BlePayload Payload
        {
            get => ReadPayload(data, "InitialValue", new() { Encoding = BleValueEncoding.Utf8, Data = Value });
            set
            {
                if (Get(data, "InitialValue") is null && value.Encoding == BleValueEncoding.Utf8) Value = value.Data;
                else WritePayload(data, "InitialValue", value);
            }
        }
        public BleCharacteristicProperties Properties { get => Choice(data, "Properties", BleCharacteristicProperties.Read); set => Set(data, "Properties", value.ToString()); }
        public BleAccessProtection ReadProtection { get => Choice<BleAccessProtection>(data, "ReadProtection"); set => Set(data, "ReadProtection", value.ToString()); }
        public BleAccessProtection WriteProtection { get => Choice<BleAccessProtection>(data, "WriteProtection"); set => Set(data, "WriteProtection", value.ToString()); }
        public string UserDescription { get => Text(data, "UserDescription"); set => Set(data, "UserDescription", value); }
        public List<Descriptor> Descriptors { get; } = Objects(data, "Descriptors").Select(item => new Descriptor(item)).ToList();
        public Descriptor AddDescriptor() { var item = new Descriptor(new() { ["Id"] = BleStandards.NewCustomUuid().ToString() }); Descriptors.Add(item); return item; }
        internal JsonObject Serialize()
        {
            var result = (JsonObject)data.DeepClone();
            if (Descriptors.Count > 0 || Get(result, "Descriptors") is not null)
                result[Key(result, "Descriptors")] = new JsonArray(Descriptors.Select(item => (JsonNode)item.Data.DeepClone()).ToArray());
            return result;
        }
    }
    public sealed class Descriptor(JsonObject data)
    {
        internal JsonObject Data => data;
        public string Id { get => Text(data, "Id"); set => Set(data, "Id", value); }
        public BlePayload Payload { get => ReadPayload(data, "Value", new()); set => WritePayload(data, "Value", value); }
        public BleAccessProtection ReadProtection { get => Choice<BleAccessProtection>(data, "ReadProtection"); set => Set(data, "ReadProtection", value.ToString()); }
    }
    private static BlePayload ReadPayload(JsonObject data, string key, BlePayload fallback)
    {
        if (Get(data, key) is null) return fallback;
        if (Get(data, key) is not JsonObject value) throw new ArgumentException($"{key} must specify Encoding and Data.");
        return new() { Encoding = Choice<BleValueEncoding>(value, "Encoding"), Data = Text(value, "Data") };
    }
    private static void WritePayload(JsonObject data, string key, BlePayload payload)
    {
        var value = Get(data, key) as JsonObject ?? new();
        if (Choice<BleValueEncoding>(value, "Encoding") != payload.Encoding) Set(value, "Encoding", payload.Encoding.ToString());
        Set(value, "Data", payload.Data);
        if (value.Parent is null) data[Key(data, key)] = value;
    }
}
