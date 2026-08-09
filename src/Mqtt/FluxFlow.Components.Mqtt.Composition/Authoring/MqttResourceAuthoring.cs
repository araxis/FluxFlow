using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Mqtt.Composition;

public abstract class MqttResourceHandle : AuthoredResourceHandle
{
    private protected MqttResourceHandle(ResourceHandle definition) : base(definition) { }
}

public sealed class MqttBrokerResourceHandle : MqttResourceHandle
{
    internal MqttBrokerResourceHandle(ResourceHandle definition) : base(definition) { }
}

public sealed class MqttRetryPolicyResourceHandle : MqttResourceHandle
{
    internal MqttRetryPolicyResourceHandle(ResourceHandle definition) : base(definition) { }
}

public sealed class MqttSubscriptionResourceHandle : MqttResourceHandle
{
    internal MqttSubscriptionResourceHandle(ResourceHandle definition) : base(definition) { }
}

public sealed class MqttClientResourceHandle : MqttResourceHandle
{
    internal MqttClientResourceHandle(ResourceHandle definition) : base(definition) { }
}

public sealed class MqttBrokerResourceBuilder
{
    public string? Host { get; set; }
    public int? Port { get; set; }
    public bool? UseTls { get; set; }
    public string? ServerName { get; set; }

    internal void Apply(ResourceDefinitionBuilder definition)
    {
        if (string.IsNullOrWhiteSpace(Host))
            throw new InvalidOperationException("MQTT broker resources require Host.");

        definition.Set(MqttComponentDefinition.ResourceProperties.Host, Host);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.Port, Port);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.UseTls, UseTls);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.ServerName, ServerName);
    }

    private static void SetIfPresent<T>(ResourceDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}

public sealed class MqttRetryPolicyResourceBuilder
{
    public MqttRetryStrategy? Strategy { get; set; }
    public TimeSpan? InitialDelay { get; set; }
    public TimeSpan? Increment { get; set; }
    public TimeSpan? MaximumDelay { get; set; }
    public int? MaximumAttempts { get; set; }
    public TimeSpan? MaximumDuration { get; set; }
    public TimeSpan? ResetAfter { get; set; }
    public double? JitterFactor { get; set; }
    public IReadOnlyList<string>? RetryCategories { get; set; }

    internal void Apply(ResourceDefinitionBuilder definition)
    {
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.Strategy, Strategy);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.InitialDelay, InitialDelay);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.Increment, Increment);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.MaximumDelay, MaximumDelay);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.MaximumAttempts, MaximumAttempts);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.MaximumDuration, MaximumDuration);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.ResetAfter, ResetAfter);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.JitterFactor, JitterFactor);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.RetryCategories, RetryCategories);
    }

    private static void SetIfPresent<T>(ResourceDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}

public sealed class MqttSubscriptionResourceBuilder
{
    public string? TopicFilter { get; set; }
    public MqttQos? Qos { get; set; }
    public bool? NoLocal { get; set; }
    public bool? RetainAsPublished { get; set; }
    public MqttRetainHandling? RetainHandling { get; set; }

    internal void Apply(ResourceDefinitionBuilder definition)
    {
        if (string.IsNullOrWhiteSpace(TopicFilter))
            throw new InvalidOperationException("MQTT subscription resources require TopicFilter.");

        definition.Set(MqttComponentDefinition.ResourceProperties.TopicFilter, TopicFilter);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.Qos, Qos);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.NoLocal, NoLocal);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.RetainAsPublished, RetainAsPublished);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.RetainHandling, RetainHandling);
    }

    private static void SetIfPresent<T>(ResourceDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}

public sealed class MqttClientResourceBuilder
{
    private readonly List<MqttSubscriptionResourceHandle> _subscriptions = [];
    private ResourceHandle? _credentialsResource;
    private ReconnectValue? _reconnect;

    public string? ClientId { get; set; }
    public MqttBrokerResourceHandle? Broker { get; set; }
    public MqttCredentialConfiguration? Credentials { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public IReadOnlyList<MqttClientCertificate>? Certificates { get; set; }
    public bool? CleanStart { get; set; }
    public TimeSpan? KeepAlive { get; set; }
    public MqttPublishMessage? LastWill { get; set; }
    public MqttAutoConnectMode? AutoConnect { get; set; }

    public void UseCredentials(ResourceHandle credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        _credentialsResource = credentials;
    }

    public void UseReconnect(MqttRetryPolicyResourceHandle retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(retryPolicy);
        _reconnect = new ReconnectValue(retryPolicy, null);
    }

    public void UseReconnect(MqttRetryPolicy retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(retryPolicy);
        _reconnect = new ReconnectValue(null, retryPolicy);
    }

    public void DisableReconnect() => _reconnect = new ReconnectValue(null, false);

    public void AddSubscription(MqttSubscriptionResourceHandle subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _subscriptions.Add(subscription);
    }

    internal void Apply(ResourceDefinitionBuilder definition)
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("MQTT client resources require ClientId.");
        if (Broker is null)
            throw new InvalidOperationException("MQTT client resources require Broker.");
        if (Credentials is not null && _credentialsResource is not null)
            throw new InvalidOperationException("MQTT client Credentials cannot be both inline and a resource reference.");

        definition.Set(MqttComponentDefinition.ResourceProperties.ClientId, ClientId);
        definition.UseResource(MqttComponentDefinition.ResourceProperties.Broker, Broker.Definition);

        if (_credentialsResource is not null)
            definition.UseResource(MqttComponentDefinition.ResourceProperties.Credentials, _credentialsResource);
        else if (Credentials is not null)
            definition.Set(MqttComponentDefinition.ResourceProperties.Credentials, Credentials);

        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.Username, Username);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.Password, Password);
        if (Certificates is not null)
        {
            definition.Set(
                MqttComponentDefinition.ResourceProperties.Certificates,
                Certificates.Select(static certificate => new
                {
                    certificate.Name,
                    ContentBase64 = Convert.ToBase64String(certificate.Content.Span),
                    certificate.Password
                }));
        }
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.CleanStart, CleanStart);
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.KeepAlive, KeepAlive);
        if (LastWill is not null)
        {
            definition.Set(
                MqttComponentDefinition.ResourceProperties.LastWill,
                new
                {
                    LastWill.Topic,
                    ContentBase64 = Convert.ToBase64String(LastWill.Content.Bytes.AsSpan()),
                    LastWill.Content.ContentType,
                    LastWill.Content.Encoding,
                    LastWill.Qos,
                    LastWill.Retain,
                    LastWill.ResponseTopic,
                    LastWill.CorrelationData,
                    LastWill.UserProperties
                });
        }
        SetIfPresent(definition, MqttComponentDefinition.ResourceProperties.AutoConnect, AutoConnect);

        if (_reconnect?.Resource is not null)
            definition.UseResource(MqttComponentDefinition.ResourceProperties.Reconnect, _reconnect.Resource.Definition);
        else if (_reconnect?.Value is not null)
            definition.Set(MqttComponentDefinition.ResourceProperties.Reconnect, _reconnect.Value);

        if (_subscriptions.Count == 1)
        {
            definition.UseResource(
                MqttComponentDefinition.ResourceProperties.Subscriptions,
                _subscriptions[0].Definition);
        }
        else if (_subscriptions.Count > 1)
        {
            definition.UseResources(
                MqttComponentDefinition.ResourceProperties.Subscriptions,
                _subscriptions.Select(static subscription => subscription.Definition));
        }
    }

    private static void SetIfPresent<T>(ResourceDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }

    private sealed record ReconnectValue(
        MqttRetryPolicyResourceHandle? Resource,
        object? Value);
}
