using System.Text.Json;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Composition.Addressing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Mqtt.Composition;

internal sealed class MqttClientResourceBinding
{
    private readonly MqttIndexedResource _resource;
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

    private MqttClientResourceBinding(
        MqttIndexedResource resource,
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

    internal static MqttClientResourceBinding Create(
        MqttIndexedResource resource,
        MqttCompositionResourceIndex resources)
    {
        var properties = resource.Definition.Properties;
        var broker = MqttCompositionConfigurationConverter.ReadRequiredReference(
            properties,
            "Broker",
            resource.Address);
        resources.RequireType(broker, MqttComponentDefinition.ResourceTypes.Broker, resource.Address);

        var subscriptions = MqttCompositionConfigurationConverter
            .ReadReferences(properties, "Subscriptions", resource.Address)
            .Select(reference =>
            {
                resources.RequireType(
                    reference,
                    MqttComponentDefinition.ResourceTypes.Subscription,
                    resource.Address);
                return new NamedResourceReference(reference.Segments[^1], reference);
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

        return new MqttClientResourceBinding(
            resource,
            broker,
            MqttCompositionConfigurationConverter.Property(properties, "Credentials"),
            MqttCompositionConfigurationConverter.StringProperty(properties, "Username"),
            MqttCompositionConfigurationConverter.StringProperty(properties, "Password"),
            MqttCompositionConfigurationConverter.Property(properties, "Certificates"),
            MqttCompositionConfigurationConverter.ValueProperty(properties, "CleanStart", true),
            MqttCompositionConfigurationConverter.ValueProperty(
                properties,
                "KeepAlive",
                TimeSpan.FromSeconds(30)),
            MqttCompositionConfigurationConverter.Property(properties, "LastWill"),
            MqttCompositionConfigurationConverter.ValueProperty(
                properties,
                "AutoConnect",
                MqttAutoConnectMode.OnStart),
            MqttCompositionConfigurationConverter.Property(properties, "Reconnect"),
            subscriptions);
    }

    internal MqttClientConfiguration CreateConfiguration(
        IServiceProvider resourceServices,
        IServiceProvider hostServices)
    {
        ArgumentNullException.ThrowIfNull(resourceServices);
        ArgumentNullException.ThrowIfNull(hostServices);
        var subscriptions = _subscriptions.ToDictionary(
            static reference => reference.Name,
            reference => resourceServices.GetRequiredKeyedService<MqttSubscriptionDefinition>(
                reference.Address.Value),
            StringComparer.Ordinal);

        return new MqttClientConfiguration
        {
            Name = _resource.Address.Value,
            ClientId = MqttCompositionConfigurationConverter.RequiredStringProperty(
                _resource.Definition.Properties,
                "ClientId",
                _resource.Address),
            Broker = resourceServices.GetRequiredKeyedService<MqttBrokerConfiguration>(_broker.Value),
            Credentials = ResolveCredentials(hostServices),
            Certificates = ResolveCertificates(hostServices),
            CleanStart = _cleanStart,
            KeepAlive = _keepAlive,
            LastWill = _lastWill is null
                ? null
                : MqttCompositionConfigurationConverter.CreateLastWill(
                    _lastWill.Value,
                    _resource.Address),
            AutoConnect = _autoConnect,
            Reconnect = ResolveReconnect(resourceServices),
            Subscriptions = subscriptions
        };
    }

    private MqttCredentialConfiguration? ResolveCredentials(IServiceProvider hostServices)
    {
        MqttCredentialConfiguration? referenced = null;
        var inline = false;
        if (_credentials is { } credentials)
        {
            if (credentials.ValueKind == JsonValueKind.String)
            {
                var reference = MqttCompositionConfigurationConverter.ParseReference(
                    credentials.GetString(),
                    _resource.Address,
                    "Credentials");
                referenced = hostServices.GetRequiredKeyedService<MqttCredentialConfiguration>(
                    reference.Value);
            }
            else if (credentials.ValueKind == JsonValueKind.Object)
            {
                MqttCompositionResourceValidator.ValidateObjectProperties(
                    credentials,
                    _resource.Address,
                    "Credentials",
                    "Username",
                    "Password");
                referenced = credentials.Deserialize<MqttCredentialConfiguration>(
                    MqttCompositionConfigurationConverter.SerializerOptions)
                    ?? throw new InvalidOperationException(
                        $"MQTT client resource '{_resource.Address}' has invalid Credentials.");
                inline = !string.IsNullOrEmpty(referenced.Password);
            }
            else
            {
                throw MqttCompositionResourceValidator.InvalidShape(
                    _resource.Address,
                    "Credentials",
                    "a resource address or object");
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
            RequireInlineSecretApproval(hostServices, "Credentials.Password");
        return resolved;
    }

    private IReadOnlyList<MqttClientCertificate> ResolveCertificates(IServiceProvider hostServices)
    {
        if (_certificates is null)
            return [];

        var elements = MqttCompositionConfigurationConverter.ScalarOrArray(
            _certificates.Value,
            _resource.Address,
            "Certificates");
        var result = new List<MqttClientCertificate>(elements.Count);
        foreach (var element in elements)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var reference = MqttCompositionConfigurationConverter.ParseReference(
                    element.GetString(),
                    _resource.Address,
                    "Certificates");
                result.Add(hostServices.GetRequiredKeyedService<MqttClientCertificate>(reference.Value));
                continue;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                throw MqttCompositionResourceValidator.InvalidShape(
                    _resource.Address,
                    "Certificates",
                    "resource addresses or objects");
            }
            MqttCompositionResourceValidator.ValidateObjectProperties(
                element,
                _resource.Address,
                "Certificates",
                "Name",
                "ContentBase64",
                "Password");
            RequireInlineSecretApproval(hostServices, "Certificates");
            result.Add(MqttCompositionConfigurationConverter.CreateCertificate(
                element,
                _resource.Address));
        }

        return result;
    }

    private MqttReconnectConfiguration ResolveReconnect(IServiceProvider resourceServices)
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
                Policy = resourceServices.GetRequiredKeyedService<MqttRetryPolicy>(
                    MqttCompositionConfigurationConverter.ParseReference(
                        value.GetString(),
                        _resource.Address,
                        "Reconnect").Value)
            },
            JsonValueKind.Object => CreateInlineReconnect(value),
            _ => throw MqttCompositionResourceValidator.InvalidShape(
                _resource.Address,
                "Reconnect",
                "a Boolean, retry resource address, or object")
        };
    }

    private MqttReconnectConfiguration CreateInlineReconnect(JsonElement value)
    {
        MqttCompositionResourceValidator.ValidateObjectProperties(
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
            Policy = value.Deserialize<MqttRetryPolicy>(
                MqttCompositionConfigurationConverter.SerializerOptions)
                ?? throw new InvalidOperationException(
                    $"MQTT client resource '{_resource.Address}' has invalid Reconnect policy.")
        };
    }

    private void RequireInlineSecretApproval(
        IServiceProvider hostServices,
        string propertyName)
    {
        var policy = hostServices.GetService<IMqttInlineSecretPolicy>();
        if (policy?.IsAllowed(_resource.Address, propertyName) != true)
        {
            throw new InvalidOperationException(
                $"MQTT client resource '{_resource.Address}' contains inline secret material in " +
                $"'{propertyName}', but the host did not approve it.");
        }
    }

    private sealed record NamedResourceReference(
        string Name,
        ApplicationAddress Address);
}
