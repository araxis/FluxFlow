using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using MQTTnet;

namespace FluxFlow.Components.Mqtt;

public sealed partial class MqttSubscriber
{
    private readonly MqttAcknowledgementMode _acknowledgementMode;
    private readonly int _maxPendingAcknowledgements;
    private readonly object _acknowledgementGate = new();
    private readonly Dictionary<string, PendingDelivery> _pending = new(StringComparer.Ordinal);
    private readonly Queue<PendingDelivery> _acknowledgementOrder = new();
    private readonly CancellationTokenRegistration _stopAcknowledgements;
    public ITargetBlock<FlowMessage> ACK { get; }

    private void ValidateAcknowledgementQoS(MqttTopic topic)
    {
        if (_acknowledgementMode == MqttAcknowledgementMode.Manual && topic.QoS != 1)
            throw new ArgumentException("Manual acknowledgement requires QoS 1 subscriptions.");
    }

    private string? AddPending(MqttApplicationMessageReceivedEventArgs args)
    {
        lock (_acknowledgementGate)
        {
            if (Stopping.IsCancellationRequested) return null;
            if (_pending.Count >= _maxPendingAcknowledgements)
                throw new InvalidOperationException("The MQTT pending acknowledgement limit was reached. No acknowledgement was sent for this delivery. Stop and rerun to recover.");
            var pending = new PendingDelivery(Guid.NewGuid().ToString("N"), _connection.Generation, args);
            _pending.Add(pending.Id, pending);
            _acknowledgementOrder.Enqueue(pending);
            return pending.Id;
        }
    }

    private async Task AcknowledgeAsync(FlowMessage message, CancellationToken cancellationToken)
    {
        MqttAcknowledgementCommand command;
        try
        {
            command = MqttJson.Read<MqttAcknowledgementCommand>(message);
            ArgumentException.ThrowIfNullOrWhiteSpace(command.AcknowledgementId);
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or ArgumentException)
        {
            RejectAcknowledgement(message, "Expected an acknowledgement command with a nonempty AcknowledgementId.");
            return;
        }

        lock (_acknowledgementGate)
        {
            if (Stopping.IsCancellationRequested || !_connection.IsConnected ||
                !_pending.TryGetValue(command.AcknowledgementId, out var delivery) ||
                delivery.Generation != _connection.Generation || delivery.Feedback is not null)
            {
                RejectAcknowledgement(message, "The acknowledgement is unknown, duplicate, or belongs to a stopped or disconnected delivery.");
                return;
            }
            delivery.Feedback = message;
        }

        // Workflow completions can arrive out of order. MQTT PUBACKs must retain receive order.
        // This runs on the single ACK consumer, never inside the native receive callback.
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Stopping);
        try
        {
            await _connection.ExecuteAsync(async (client, token) =>
            {
                while (true)
                {
                    PendingDelivery delivery;
                    lock (_acknowledgementGate)
                    {
                        if (Stopping.IsCancellationRequested || !client.IsConnected ||
                            !_acknowledgementOrder.TryPeek(out delivery!) || delivery.Feedback is null ||
                            delivery.Generation != _connection.Generation) return true;
                    }
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(_connection.OperationTimeout);
                    await delivery.Args.AcknowledgeAsync(timeout.Token).ConfigureAwait(false);
                    lock (_acknowledgementGate)
                    {
                        // Stop or connection loss can clear the queue while the native send completes.
                        if (!_pending.Remove(delivery.Id)) return true;
                        _acknowledgementOrder.Dequeue();
                    }
                    // An earlier ACK can release several deliveries. Keep each feedback's own lineage.
                    EmitEvent(delivery.Feedback.WithValue(new FlowEvent
                    {
                        Name = "mqtt.acknowledged", Kind = FlowEventKind.Domain,
                        Details = MqttJson.Value(new { AcknowledgementId = delivery.Id })
                    }, delivery.Feedback.Headers));
                }
            }, stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested) { }
    }

    private void RejectAcknowledgement(FlowMessage message, string reason) => EmitEvent(message.WithValue(new FlowEvent
    {
        Name = "mqtt.acknowledgement_rejected", Kind = FlowEventKind.Diagnostic,
        Level = FlowEventLevel.Warning, Message = reason, Details = message.Value
    }));

    private void OnConnectionStateChanged(FlowEvent state)
    {
        if (state.Name is "mqtt.connecting" or "mqtt.disconnecting" or "mqtt.disconnected")
            ClearPending("The MQTT connection changed.");
    }

    private void ClearPending(string reason)
    {
        int count;
        lock (_acknowledgementGate)
        {
            count = _pending.Count;
            _pending.Clear();
            _acknowledgementOrder.Clear();
        }
        if (count != 0) EmitEvent(new FlowEvent
        {
            Name = "mqtt.acknowledgements_abandoned", Kind = FlowEventKind.Diagnostic,
            Level = FlowEventLevel.Warning, Message = reason, Details = MqttJson.Value(new { Count = count })
        });
    }

    private sealed class PendingDelivery(string id, long generation, MqttApplicationMessageReceivedEventArgs args)
    {
        public string Id { get; } = id;
        public long Generation { get; } = generation;
        public MqttApplicationMessageReceivedEventArgs Args { get; } = args;
        public FlowMessage? Feedback { get; set; }
    }
}
