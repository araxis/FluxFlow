using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;

namespace FluxFlow.Components.Mqtt.Client;

internal static class MqttClientConfigurationValidator
{
    internal static MqttClientConfiguration Validate(MqttClientConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateName(configuration.Name, nameof(configuration.Name));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            configuration.ClientId,
            nameof(configuration.ClientId));
        ArgumentNullException.ThrowIfNull(configuration.Broker);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            configuration.Broker.Host,
            nameof(configuration.Broker.Host));
        if (configuration.Broker.Port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(configuration.Broker.Port));
        if (!Enum.IsDefined(configuration.Broker.Transport))
            throw new ArgumentOutOfRangeException(nameof(configuration.Broker.Transport));
        if (configuration.Broker.Transport == MqttBrokerTransport.WebSocket)
        {
            ValidateWebSocketBroker(configuration.Broker);
        }
        else if (!string.Equals(
                     configuration.Broker.WebSocketPath,
                     "/mqtt",
                     StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "MQTT WebSocketPath can only be customized for WebSocket transport.",
                nameof(configuration.Broker.WebSocketPath));
        }
        if (configuration.KeepAlive <= TimeSpan.Zero ||
            configuration.KeepAlive > TimeSpan.FromSeconds(ushort.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(configuration.KeepAlive));
        if (!Enum.IsDefined(configuration.AutoConnect))
            throw new ArgumentOutOfRangeException(nameof(configuration.AutoConnect));

        ValidateRetryPolicy(configuration.Reconnect.Policy);
        if (configuration.LastWill is not null)
            ValidatePublishMessage(configuration.LastWill);

        foreach (var certificate in configuration.Certificates)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            ArgumentException.ThrowIfNullOrWhiteSpace(certificate.Name);
            if (certificate.Content.IsEmpty)
            {
                throw new ArgumentException(
                    $"MQTT client certificate '{certificate.Name}' has no content.",
                    nameof(configuration.Certificates));
            }
        }

        foreach (var subscription in configuration.Subscriptions)
        {
            ValidateName(subscription.Key, nameof(configuration.Subscriptions));
            ValidateSubscription(subscription.Value);
        }

        return configuration;
    }

    private static void ValidateWebSocketBroker(MqttBrokerConfiguration broker)
    {
        if (string.IsNullOrWhiteSpace(broker.WebSocketPath) ||
            broker.WebSocketPath[0] != '/' ||
            broker.WebSocketPath.Contains('?') ||
            broker.WebSocketPath.Contains('#'))
        {
            throw new ArgumentException(
                "MQTT WebSocketPath must be an absolute path without a query or fragment.",
                nameof(broker.WebSocketPath));
        }

        if (!string.IsNullOrWhiteSpace(broker.ServerName))
        {
            throw new NotSupportedException(
                "MQTT ServerName overrides are not portable for WebSocket transport. " +
                "Use the broker Host as the WSS server name.");
        }
    }

    internal static void ValidateTriggerOptions(
        MqttTriggerRegistrationOptions options,
        Func<MqttSubscriptionTarget, MqttSubscriptionDefinition?> resolveDefinition,
        MqttTransportCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolveDefinition);
        ArgumentNullException.ThrowIfNull(capabilities);
        ValidateName(options.TriggerId, nameof(options.TriggerId));
        if (options.Subscriptions.Count == 0)
            throw new ArgumentException("An MQTT trigger requires at least one subscription.", nameof(options));
        if (options.MaximumPendingMessages <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumPendingMessages));
        if (!Enum.IsDefined(options.WorkflowAcknowledgement))
            throw new ArgumentOutOfRangeException(nameof(options.WorkflowAcknowledgement));
        if (!Enum.IsDefined(options.BrokerAcknowledgement))
            throw new ArgumentOutOfRangeException(nameof(options.BrokerAcknowledgement));
        if (options.BrokerAcknowledgement == MqttBrokerAcknowledgement.AfterOutcome &&
            options.WorkflowAcknowledgement != MqttWorkflowAcknowledgement.Required)
        {
            throw new ArgumentException(
                "Broker acknowledgement after outcome requires workflow acknowledgement.",
                nameof(options));
        }

        var canReceiveAcknowledgedDelivery = options.Subscriptions.Any(target =>
        {
            var definition = resolveDefinition(target);
            return definition is null || definition.Qos != MqttQos.AtMostOnce;
        });
        if (canReceiveAcknowledgedDelivery &&
            options.BrokerAcknowledgement != MqttBrokerAcknowledgement.Automatic &&
            !capabilities.DeferredAcknowledgement)
        {
            throw new NotSupportedException(
                "The MQTT transport does not support deferred broker acknowledgement.");
        }
        if (canReceiveAcknowledgedDelivery &&
            options.BrokerAcknowledgement == MqttBrokerAcknowledgement.AfterOutcome &&
            !capabilities.NegativeAcknowledgement)
        {
            throw new NotSupportedException(
                "The MQTT transport does not support negative broker acknowledgement.");
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in options.Subscriptions)
        {
            if (!identities.Add(target.Identity))
            {
                throw new ArgumentException(
                    $"MQTT trigger subscription '{target.Identity}' is duplicated.",
                    nameof(options));
            }
            if (target.Inline is not null)
                ValidateSubscription(target.Inline);
        }
    }

    internal static void ValidateSubscription(MqttSubscriptionDefinition subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.TopicFilter);
        var validation = Validation.MqttTopicValidator.ValidateSubscriptionFilter(
            subscription.TopicFilter);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Message, nameof(subscription));
        if (!Enum.IsDefined(subscription.Qos))
            throw new ArgumentOutOfRangeException(nameof(subscription.Qos));
        if (!Enum.IsDefined(subscription.RetainHandling))
            throw new ArgumentOutOfRangeException(nameof(subscription.RetainHandling));
    }

    internal static void ValidateName(string value, string parameterName)
        => ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

    internal static void ValidatePublishMessage(MqttPublishMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var validation = Validation.MqttTopicValidator.ValidatePublishTopic(message.Topic);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Message, nameof(message));
        ArgumentNullException.ThrowIfNull(message.Content);
        if (!Enum.IsDefined(message.Qos))
            throw new ArgumentOutOfRangeException(nameof(message.Qos));
        if (!string.IsNullOrWhiteSpace(message.ResponseTopic))
        {
            var responseValidation = Validation.MqttTopicValidator.ValidatePublishTopic(
                message.ResponseTopic);
            if (!responseValidation.IsValid)
                throw new ArgumentException(responseValidation.Message, nameof(message));
        }
    }

    private static void ValidateRetryPolicy(MqttRetryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!Enum.IsDefined(policy.Strategy))
            throw new ArgumentOutOfRangeException(nameof(policy.Strategy));
        if (policy.InitialDelay < TimeSpan.Zero ||
            policy.Increment < TimeSpan.Zero ||
            policy.MaximumDelay < TimeSpan.Zero ||
            policy.ResetAfter < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }
        if (policy.MaximumAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy.MaximumAttempts));
        if (policy.MaximumDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(policy.MaximumDuration));
        if (policy.JitterFactor is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(policy.JitterFactor));
    }
}
