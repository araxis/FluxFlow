using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using System.Threading.Channels;

namespace FluxFlow.Components.Mqtt.Client;

internal sealed class MqttReceivedMessageDispatcher(
    MqttClientSubscriptionState subscriptions)
{
    private readonly MqttClientSubscriptionState _subscriptions =
        subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));

    internal async Task DispatchAsync(
        IMqttTransportSession session,
        MqttTransportReceivedMessage received,
        CancellationToken cancellationToken)
    {
        var targets = _subscriptions.Match(received.Message.Topic);
        if (received.Message.Qos == MqttQos.AtMostOnce || received.Delivery.IsEmpty)
        {
            await Task.WhenAll(targets.Select(target => DispatchToTriggerAsync(
                target.Registration,
                target.Matches,
                received,
                acknowledgement: null,
                cancellationToken))).ConfigureAwait(false);
            return;
        }

        if (targets.Length == 0)
        {
            await session.AcknowledgeAsync(
                received.Delivery,
                MqttWorkflowOutcome.Ack,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var acknowledgement = new BrokerAcknowledgementCoordinator(
            session,
            received.Delivery,
            targets.Length);
        await Task.WhenAll(targets.Select(target => DispatchToTriggerAsync(
            target.Registration,
            target.Matches,
            received,
            acknowledgement,
            cancellationToken))).ConfigureAwait(false);
    }

    private static async Task DispatchToTriggerAsync(
        MqttTriggerRegistration registration,
        string[] matches,
        MqttTransportReceivedMessage received,
        BrokerAcknowledgementCoordinator? acknowledgement,
        CancellationToken cancellationToken)
    {
        MqttTriggerDelivery? delivery = null;
        try
        {
            var message = received.Message with { MatchedSubscriptions = matches };
            delivery = new MqttTriggerDelivery(
                message,
                acknowledgement is null
                    ? static (_, _) => ValueTask.CompletedTask
                    : acknowledgement.CompleteAsync);
            await registration.WriteAsync(delivery, cancellationToken).ConfigureAwait(false);

            if (registration.Options.BrokerAcknowledgement ==
                MqttBrokerAcknowledgement.Automatic)
            {
                await delivery.CompleteBrokerAcknowledgementAsync(
                    MqttWorkflowOutcome.Ack,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ChannelClosedException)
        {
            // A trigger revision can close after the dispatch snapshot was captured.
            if (delivery is not null)
            {
                await delivery.CompleteBrokerAcknowledgementAsync(
                    MqttWorkflowOutcome.Nak,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private sealed class BrokerAcknowledgementCoordinator(
        IMqttTransportSession session,
        MqttTransportDeliveryToken delivery,
        int participants)
    {
        private readonly object _gate = new();
        private int _remaining = participants;
        private MqttWorkflowOutcome _outcome = MqttWorkflowOutcome.Ack;

        internal ValueTask CompleteAsync(
            MqttWorkflowOutcome outcome,
            CancellationToken cancellationToken)
        {
            MqttWorkflowOutcome finalOutcome;
            lock (_gate)
            {
                if (_remaining <= 0)
                    return ValueTask.CompletedTask;

                if (Priority(outcome) > Priority(_outcome))
                    _outcome = outcome;
                _remaining--;
                if (_remaining != 0)
                    return ValueTask.CompletedTask;
                finalOutcome = _outcome;
            }

            return session.AcknowledgeAsync(delivery, finalOutcome, cancellationToken);
        }

        private static int Priority(MqttWorkflowOutcome outcome)
            => outcome switch
            {
                MqttWorkflowOutcome.Ack => 0,
                MqttWorkflowOutcome.Timeout => 1,
                MqttWorkflowOutcome.Nak => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(outcome))
            };
    }
}
