using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;

namespace FluxFlow.Components.Mqtt.Composition;

internal static class MqttCompositionConfigurationConverter
{
    internal static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    internal static T Deserialize<T>(IReadOnlyDictionary<string, JsonElement> properties)
    {
        var json = JsonSerializer.Serialize(properties, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
               ?? throw new InvalidOperationException(
                   $"MQTT configuration could not be bound to {typeof(T).Name}.");
    }

    internal static JsonElement? Property(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
        => properties.TryGetValue(name, out var value) ? value : null;

    internal static string? StringProperty(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"MQTT property '{name}' must be a string.");
        return value.GetString();
    }

    internal static string RequiredStringProperty(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        ApplicationAddress resource)
        => StringProperty(properties, name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"MQTT resource '{resource}' requires a non-empty '{name}' property.");

    internal static T ValueProperty<T>(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        T defaultValue)
        => properties.TryGetValue(name, out var value)
            ? value.Deserialize<T>(SerializerOptions)!
            : defaultValue;

    internal static ApplicationAddress ReadRequiredReference(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        ApplicationAddress owner)
    {
        if (!properties.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"MQTT resource '{owner}' requires resource-address property '{name}'.");
        }

        return ParseReference(value.GetString(), owner, name);
    }

    internal static IReadOnlyList<ApplicationAddress> ReadReferences(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        ApplicationAddress owner)
    {
        if (!properties.TryGetValue(name, out var value))
            return [];
        return ScalarOrArray(value, owner, name)
            .Select(element => element.ValueKind == JsonValueKind.String
                ? ParseReference(element.GetString(), owner, name)
                : throw MqttCompositionResourceValidator.InvalidShape(
                    owner,
                    name,
                    "a resource address or array of addresses"))
            .ToArray();
    }

    internal static IReadOnlyList<JsonElement> ScalarOrArray(
        JsonElement value,
        ApplicationAddress owner,
        string propertyName)
        => value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : value.ValueKind is JsonValueKind.String or JsonValueKind.Object
                ? [value]
                : throw MqttCompositionResourceValidator.InvalidShape(
                    owner,
                    propertyName,
                    "a scalar value or array");

    internal static ApplicationAddress ParseReference(
        string? value,
        ApplicationAddress owner,
        string propertyName)
    {
        if (!ApplicationAddress.TryParse(value, out var address) ||
            address!.Kind != ApplicationAddressKind.Resource)
        {
            throw new InvalidOperationException(
                $"MQTT resource '{owner}' property '{propertyName}' requires a canonical Resources address.");
        }

        return address;
    }

    internal static MqttClientCertificate CreateCertificate(
        JsonElement element,
        ApplicationAddress client)
    {
        var binding = element.Deserialize<CertificateBinding>(SerializerOptions)
            ?? throw new InvalidOperationException(
                $"MQTT client resource '{client}' has an invalid certificate entry.");
        if (string.IsNullOrWhiteSpace(binding.Name) || string.IsNullOrWhiteSpace(binding.ContentBase64))
        {
            throw new InvalidOperationException(
                $"MQTT client resource '{client}' inline certificates require Name and ContentBase64.");
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(binding.ContentBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"MQTT client resource '{client}' certificate ContentBase64 is invalid.",
                exception);
        }

        return new MqttClientCertificate
        {
            Name = binding.Name,
            Content = content,
            Password = binding.Password
        };
    }

    internal static MqttPublishMessage CreateLastWill(
        JsonElement element,
        ApplicationAddress client)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw MqttCompositionResourceValidator.InvalidShape(
                client,
                "LastWill",
                "an object");
        }
        MqttCompositionResourceValidator.ValidateObjectProperties(
            element,
            client,
            "LastWill",
            "Topic",
            "Content",
            "ContentBase64",
            "ContentType",
            "Encoding",
            "Qos",
            "Retain",
            "ResponseTopic",
            "CorrelationData",
            "UserProperties");
        var binding = element.Deserialize<LastWillBinding>(SerializerOptions)
            ?? throw new InvalidOperationException(
                $"MQTT client resource '{client}' has an invalid LastWill.");
        if (string.IsNullOrWhiteSpace(binding.Topic))
            throw new InvalidOperationException($"MQTT client resource '{client}' LastWill requires Topic.");
        if ((binding.Content is null) == (binding.ContentBase64 is null))
        {
            throw new InvalidOperationException(
                $"MQTT client resource '{client}' LastWill requires exactly one of Content or ContentBase64.");
        }

        byte[] bytes;
        if (binding.ContentBase64 is not null)
        {
            try
            {
                bytes = Convert.FromBase64String(binding.ContentBase64);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    $"MQTT client resource '{client}' LastWill ContentBase64 is invalid.",
                    exception);
            }
        }
        else
        {
            try
            {
                bytes = Encoding.GetEncoding(binding.Encoding ?? "utf-8").GetBytes(binding.Content!);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    $"MQTT client resource '{client}' LastWill Encoding is invalid.",
                    exception);
            }
        }

        return new MqttPublishMessage
        {
            Topic = binding.Topic,
            Content = FlowContent.FromBytes(bytes, binding.ContentType, binding.Encoding),
            Qos = binding.Qos,
            Retain = binding.Retain,
            ResponseTopic = binding.ResponseTopic,
            CorrelationData = binding.CorrelationData,
            UserProperties = binding.UserProperties
        };
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNameCaseInsensitive = false
        };
        options.Converters.Add(new JsonStringEnumConverter(
            namingPolicy: null,
            allowIntegerValues: false));
        return options;
    }

    private sealed record CertificateBinding
    {
        public string Name { get; init; } = string.Empty;

        public string ContentBase64 { get; init; } = string.Empty;

        public string? Password { get; init; }
    }

    private sealed record LastWillBinding
    {
        public string Topic { get; init; } = string.Empty;

        public string? Content { get; init; }

        public string? ContentBase64 { get; init; }

        public string? ContentType { get; init; }

        public string? Encoding { get; init; }

        public MqttQos Qos { get; init; }

        public bool Retain { get; init; }

        public string? ResponseTopic { get; init; }

        public string? CorrelationData { get; init; }

        public IReadOnlyDictionary<string, string> UserProperties { get; init; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
