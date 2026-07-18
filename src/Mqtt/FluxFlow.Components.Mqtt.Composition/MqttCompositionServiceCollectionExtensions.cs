using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttCompositionServiceCollectionExtensions
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static IServiceCollection AddMqttCompositionResources(
        this IServiceCollection services,
        ApplicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);

        var resources = FlattenResources(definition);
        foreach (var resource in resources.Values.OrderBy(static value => value.Address.Value, StringComparer.Ordinal))
        {
            switch (resource.Definition.Type)
            {
                case MqttCompositionResourceTypes.Broker:
                    RegisterBroker(services, resource);
                    break;
                case MqttCompositionResourceTypes.Retry:
                    RegisterRetry(services, resource);
                    break;
                case MqttCompositionResourceTypes.Subscription:
                    RegisterSubscription(services, resource);
                    break;
                case MqttCompositionResourceTypes.Client:
                    RegisterClient(services, resource, resources);
                    break;
            }
        }

        return services;
    }

    private static void RegisterBroker(IServiceCollection services, IndexedResource resource)
    {
        ValidateProperties(resource, "Host", "Port", "UseTls", "ServerName");
        var configuration = Deserialize<MqttBrokerConfiguration>(resource.Definition.Properties);
        if (!HasKeyedService<MqttBrokerConfiguration>(services, resource.Address.Value))
            services.AddKeyedSingleton(resource.Address.Value, configuration);
    }

    private static void RegisterRetry(IServiceCollection services, IndexedResource resource)
    {
        ValidateProperties(
            resource,
            "Strategy",
            "InitialDelay",
            "Increment",
            "MaximumDelay",
            "MaximumAttempts",
            "MaximumDuration",
            "ResetAfter",
            "JitterFactor",
            "RetryCategories");
        var policy = Deserialize<MqttRetryPolicy>(resource.Definition.Properties);
        if (!HasKeyedService<MqttRetryPolicy>(services, resource.Address.Value))
            services.AddKeyedSingleton(resource.Address.Value, policy);
    }

    private static void RegisterSubscription(IServiceCollection services, IndexedResource resource)
    {
        ValidateProperties(
            resource,
            "TopicFilter",
            "Qos",
            "NoLocal",
            "RetainAsPublished",
            "RetainHandling");
        var subscription = Deserialize<MqttSubscriptionDefinition>(resource.Definition.Properties);
        if (!HasKeyedService<MqttSubscriptionDefinition>(services, resource.Address.Value))
            services.AddKeyedSingleton(resource.Address.Value, subscription);
    }

    private static void RegisterClient(
        IServiceCollection services,
        IndexedResource resource,
        IReadOnlyDictionary<ApplicationAddress, IndexedResource> resources)
    {
        ValidateProperties(
            resource,
            "ClientId",
            "Broker",
            "Credentials",
            "Username",
            "Password",
            "Certificates",
            "CleanStart",
            "KeepAlive",
            "LastWill",
            "AutoConnect",
            "Reconnect",
            "Subscriptions");

        var binding = ClientBinding.Create(resource, resources);
        if (!HasKeyedService<MqttClientConfiguration>(services, resource.Address.Value))
        {
            services.AddKeyedSingleton<MqttClientConfiguration>(
                resource.Address.Value,
                (provider, _) => binding.CreateConfiguration(provider));
        }

        if (!HasKeyedService<IMqttClientController>(services, resource.Address.Value))
        {
            services.AddKeyedSingleton<IMqttClientController>(
                resource.Address.Value,
                (provider, _) => new MqttClientController(
                    provider.GetRequiredKeyedService<MqttClientConfiguration>(resource.Address.Value),
                    ResolveTransportFactory(provider, resource.Address),
                    ResolveClock(provider, resource.Address)));
        }
    }

    private static IMqttTransportFactory ResolveTransportFactory(
        IServiceProvider provider,
        ApplicationAddress client)
        => provider.GetKeyedService<IMqttTransportFactory>(client.Value)
           ?? provider.GetService<IMqttTransportFactory>()
           ?? throw new InvalidOperationException(
               $"MQTT client resource '{client}' requires an {nameof(IMqttTransportFactory)} " +
               "registered for its resource address or as the host default.");

    private static TimeProvider ResolveClock(
        IServiceProvider provider,
        ApplicationAddress client)
        => provider.GetKeyedService<TimeProvider>(client.Value)
           ?? provider.GetService<TimeProvider>()
           ?? TimeProvider.System;

    private static IReadOnlyDictionary<ApplicationAddress, IndexedResource> FlattenResources(
        ApplicationDefinition definition)
    {
        var result = new Dictionary<ApplicationAddress, IndexedResource>();
        foreach (var (name, resource) in definition.Resources)
            FlattenResource([name], resource, result);
        return result;
    }

    private static void FlattenResource(
        IReadOnlyList<string> path,
        ResourceDefinition resource,
        IDictionary<ApplicationAddress, IndexedResource> result)
    {
        if (resource is ResourceInstanceDefinition instance)
        {
            var address = ApplicationAddress.Resource(path.ToArray());
            result.Add(address, new IndexedResource(address, instance));
            return;
        }

        var group = (ResourceGroupDefinition)resource;
        foreach (var (name, child) in group.Resources)
            FlattenResource([.. path, name], child, result);
    }

    private static void ValidateProperties(IndexedResource resource, params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        var unknown = resource.Definition.Properties.Keys
            .Where(property => !names.Contains(property))
            .OrderBy(static property => property, StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"MQTT resource '{resource.Address}' has unsupported properties: " +
                string.Join(", ", unknown));
        }
    }

    private static T Deserialize<T>(IReadOnlyDictionary<string, JsonElement> properties)
    {
        var json = JsonSerializer.Serialize(properties, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
               ?? throw new InvalidOperationException(
                   $"MQTT configuration could not be bound to {typeof(T).Name}.");
    }

    private static bool HasKeyedService<TService>(IServiceCollection services, object key)
        => services.Any(descriptor =>
            descriptor.ServiceType == typeof(TService) &&
            descriptor.IsKeyedService &&
            Equals(descriptor.ServiceKey, key));

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNameCaseInsensitive = false
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record IndexedResource(
        ApplicationAddress Address,
        ResourceInstanceDefinition Definition);

    private sealed class ClientBinding
    {
        private readonly IndexedResource _resource;
        private readonly ApplicationAddress _broker;
        private readonly JsonElement? _credentials;
        private readonly string? _username;
        private readonly string? _password;
        private readonly JsonElement? _certificates;
        private readonly bool _cleanStart;
        private readonly TimeSpan _keepAlive;
        private readonly JsonElement? _lastWill;
        private readonly MqttAutoConnectMode _autoConnect;
        private readonly JsonElement? _reconnect;
        private readonly IReadOnlyList<NamedResourceReference> _subscriptions;

        private ClientBinding(
            IndexedResource resource,
            ApplicationAddress broker,
            JsonElement? credentials,
            string? username,
            string? password,
            JsonElement? certificates,
            bool cleanStart,
            TimeSpan keepAlive,
            JsonElement? lastWill,
            MqttAutoConnectMode autoConnect,
            JsonElement? reconnect,
            IReadOnlyList<NamedResourceReference> subscriptions)
        {
            _resource = resource;
            _broker = broker;
            _credentials = credentials;
            _username = username;
            _password = password;
            _certificates = certificates;
            _cleanStart = cleanStart;
            _keepAlive = keepAlive;
            _lastWill = lastWill;
            _autoConnect = autoConnect;
            _reconnect = reconnect;
            _subscriptions = subscriptions;
        }

        public static ClientBinding Create(
            IndexedResource resource,
            IReadOnlyDictionary<ApplicationAddress, IndexedResource> resources)
        {
            var properties = resource.Definition.Properties;
            var broker = ReadRequiredReference(properties, "Broker", resource.Address);
            RequireType(broker, MqttCompositionResourceTypes.Broker, resources, resource.Address);

            var subscriptions = ReadReferences(properties, "Subscriptions", resource.Address)
                .Select(reference =>
                {
                    RequireType(
                        reference,
                        MqttCompositionResourceTypes.Subscription,
                        resources,
                        resource.Address);
                    return new NamedResourceReference(
                        reference.Segments[^1],
                        reference);
                })
                .ToArray();

            var duplicateSubscription = subscriptions
                .GroupBy(static reference => reference.Name, StringComparer.Ordinal)
                .FirstOrDefault(static group => group.Count() > 1);
            if (duplicateSubscription is not null)
            {
                throw new InvalidOperationException(
                    $"MQTT client resource '{resource.Address}' references multiple subscriptions " +
                    $"named '{duplicateSubscription.Key}'. Subscription resource leaf names must " +
                    "be unique per client.");
            }

            return new ClientBinding(
                resource,
                broker,
                Property(properties, "Credentials"),
                StringProperty(properties, "Username"),
                StringProperty(properties, "Password"),
                Property(properties, "Certificates"),
                ValueProperty(properties, "CleanStart", true),
                ValueProperty(properties, "KeepAlive", TimeSpan.FromSeconds(30)),
                Property(properties, "LastWill"),
                ValueProperty(properties, "AutoConnect", MqttAutoConnectMode.OnStart),
                Property(properties, "Reconnect"),
                subscriptions);
        }

        public MqttClientConfiguration CreateConfiguration(IServiceProvider provider)
        {
            var credentials = ResolveCredentials(provider);
            var reconnect = ResolveReconnect(provider);
            var subscriptions = _subscriptions.ToDictionary(
                static reference => reference.Name,
                reference => provider.GetRequiredKeyedService<MqttSubscriptionDefinition>(
                    reference.Address.Value),
                StringComparer.Ordinal);

            return new MqttClientConfiguration
            {
                Name = _resource.Address.Value,
                ClientId = RequiredStringProperty(
                    _resource.Definition.Properties,
                    "ClientId",
                    _resource.Address),
                Broker = provider.GetRequiredKeyedService<MqttBrokerConfiguration>(_broker.Value),
                Credentials = credentials,
                Certificates = ResolveCertificates(provider),
                CleanStart = _cleanStart,
                KeepAlive = _keepAlive,
                LastWill = _lastWill is null ? null : CreateLastWill(_lastWill.Value, _resource.Address),
                AutoConnect = _autoConnect,
                Reconnect = reconnect,
                Subscriptions = subscriptions
            };
        }

        private MqttCredentialConfiguration? ResolveCredentials(IServiceProvider provider)
        {
            MqttCredentialConfiguration? referenced = null;
            var inline = false;
            if (_credentials is { } credentials)
            {
                if (credentials.ValueKind == JsonValueKind.String)
                {
                    var reference = ParseReference(credentials.GetString(), _resource.Address, "Credentials");
                    referenced = provider.GetRequiredKeyedService<MqttCredentialConfiguration>(reference.Value);
                }
                else if (credentials.ValueKind == JsonValueKind.Object)
                {
                    ValidateObjectProperties(
                        credentials,
                        _resource.Address,
                        "Credentials",
                        "Username",
                        "Password");
                    referenced = credentials.Deserialize<MqttCredentialConfiguration>(SerializerOptions)
                        ?? throw new InvalidOperationException(
                            $"MQTT client resource '{_resource.Address}' has invalid Credentials.");
                    inline = !string.IsNullOrEmpty(referenced.Password);
                }
                else
                {
                    throw InvalidShape(_resource.Address, "Credentials", "a resource address or object");
                }
            }

            var resolved = referenced;
            if (_username is not null || _password is not null)
            {
                resolved = new MqttCredentialConfiguration
                {
                    Username = _username ?? referenced?.Username,
                    Password = _password ?? referenced?.Password
                };
                inline |= _password is not null;
            }

            if (inline)
                RequireInlineSecretApproval(provider, _resource.Address, "Credentials.Password");
            return resolved;
        }

        private IReadOnlyList<MqttClientCertificate> ResolveCertificates(IServiceProvider provider)
        {
            if (_certificates is null)
                return [];

            var elements = ScalarOrArray(_certificates.Value, _resource.Address, "Certificates");
            var result = new List<MqttClientCertificate>(elements.Count);
            foreach (var element in elements)
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var reference = ParseReference(element.GetString(), _resource.Address, "Certificates");
                    result.Add(provider.GetRequiredKeyedService<MqttClientCertificate>(reference.Value));
                    continue;
                }

                if (element.ValueKind != JsonValueKind.Object)
                    throw InvalidShape(_resource.Address, "Certificates", "resource addresses or objects");
                ValidateObjectProperties(
                    element,
                    _resource.Address,
                    "Certificates",
                    "Name",
                    "ContentBase64",
                    "Password");
                RequireInlineSecretApproval(provider, _resource.Address, "Certificates");
                result.Add(CreateCertificate(element, _resource.Address));
            }

            return result;
        }

        private MqttReconnectConfiguration ResolveReconnect(IServiceProvider provider)
        {
            if (_reconnect is null)
                return new MqttReconnectConfiguration();

            var value = _reconnect.Value;
            return value.ValueKind switch
            {
                JsonValueKind.True => new MqttReconnectConfiguration(),
                JsonValueKind.False => new MqttReconnectConfiguration { Enabled = false },
                JsonValueKind.String => new MqttReconnectConfiguration
                {
                    Policy = provider.GetRequiredKeyedService<MqttRetryPolicy>(
                        ParseReference(value.GetString(), _resource.Address, "Reconnect").Value)
                },
                JsonValueKind.Object => CreateInlineReconnect(value),
                _ => throw InvalidShape(
                    _resource.Address,
                    "Reconnect",
                    "a Boolean, retry resource address, or object")
            };
        }

        private MqttReconnectConfiguration CreateInlineReconnect(JsonElement value)
        {
            ValidateObjectProperties(
                value,
                _resource.Address,
                "Reconnect",
                "Strategy",
                "InitialDelay",
                "Increment",
                "MaximumDelay",
                "MaximumAttempts",
                "MaximumDuration",
                "ResetAfter",
                "JitterFactor",
                "RetryCategories");
            return new MqttReconnectConfiguration
            {
                Policy = value.Deserialize<MqttRetryPolicy>(SerializerOptions)
                         ?? throw new InvalidOperationException(
                             $"MQTT client resource '{_resource.Address}' has invalid Reconnect policy.")
            };
        }
    }

    private static JsonElement? Property(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
        => properties.TryGetValue(name, out var value) ? value : null;

    private static string? StringProperty(
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

    private static string RequiredStringProperty(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        ApplicationAddress resource)
        => StringProperty(properties, name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"MQTT resource '{resource}' requires a non-empty '{name}' property.");

    private static T ValueProperty<T>(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        T defaultValue)
        => properties.TryGetValue(name, out var value)
            ? value.Deserialize<T>(SerializerOptions)!
            : defaultValue;

    private static ApplicationAddress ReadRequiredReference(
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

    private static IReadOnlyList<ApplicationAddress> ReadReferences(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        ApplicationAddress owner)
    {
        if (!properties.TryGetValue(name, out var value))
            return [];
        return ScalarOrArray(value, owner, name)
            .Select(element => element.ValueKind == JsonValueKind.String
                ? ParseReference(element.GetString(), owner, name)
                : throw InvalidShape(owner, name, "a resource address or array of addresses"))
            .ToArray();
    }

    private static IReadOnlyList<JsonElement> ScalarOrArray(
        JsonElement value,
        ApplicationAddress owner,
        string propertyName)
        => value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : value.ValueKind is JsonValueKind.String or JsonValueKind.Object
                ? [value]
                : throw InvalidShape(owner, propertyName, "a scalar value or array");

    private static ApplicationAddress ParseReference(
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

    private static void RequireType(
        ApplicationAddress reference,
        string expectedType,
        IReadOnlyDictionary<ApplicationAddress, IndexedResource> resources,
        ApplicationAddress owner)
    {
        if (!resources.TryGetValue(reference, out var resource))
        {
            throw new InvalidOperationException(
                $"MQTT resource '{owner}' references missing resource '{reference}'.");
        }
        if (!string.Equals(resource.Definition.Type, expectedType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MQTT resource '{owner}' references '{reference}' as '{expectedType}', " +
                $"but its type is '{resource.Definition.Type}'.");
        }
    }

    private static void RequireInlineSecretApproval(
        IServiceProvider provider,
        ApplicationAddress client,
        string propertyName)
    {
        var policy = provider.GetService<IMqttInlineSecretPolicy>();
        if (policy?.IsAllowed(client, propertyName) != true)
        {
            throw new InvalidOperationException(
                $"MQTT client resource '{client}' contains inline secret material in " +
                $"'{propertyName}', but the host did not approve it.");
        }
    }

    private static MqttClientCertificate CreateCertificate(
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

    private static MqttPublishMessage CreateLastWill(
        JsonElement element,
        ApplicationAddress client)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw InvalidShape(client, "LastWill", "an object");
        ValidateObjectProperties(
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
            Content = FluxFlow.Data.FlowContent.FromBytes(
                bytes,
                binding.ContentType,
                binding.Encoding),
            Qos = binding.Qos,
            Retain = binding.Retain,
            ResponseTopic = binding.ResponseTopic,
            CorrelationData = binding.CorrelationData,
            UserProperties = binding.UserProperties
        };
    }

    private static InvalidOperationException InvalidShape(
        ApplicationAddress owner,
        string propertyName,
        string expected)
        => new($"MQTT resource '{owner}' property '{propertyName}' must be {expected}.");

    private static void ValidateObjectProperties(
        JsonElement value,
        ApplicationAddress owner,
        string propertyName,
        params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        var unknown = value.EnumerateObject()
            .Select(static property => property.Name)
            .Where(property => !names.Contains(property))
            .OrderBy(static property => property, StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"MQTT resource '{owner}' property '{propertyName}' has unsupported properties: " +
                string.Join(", ", unknown));
        }
    }

    private sealed record NamedResourceReference(
        string Name,
        ApplicationAddress Address);

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
