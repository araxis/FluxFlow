using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Nodes;
using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttSubscriptionTriggerNode : IFlowSource
{
    private readonly IMqttClientController _controller;
    private readonly MqttSubscriptionTriggerOptions _options;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<TraceId, PendingOutcome> _pending = new();
    private readonly MqttSourcePump<MqttReceivedApplicationMessage> _pump;
    private IMqttTriggerRegistration? _registration;

    public MqttSubscriptionTriggerNode(
        IMqttClientController controller,
        MqttSubscriptionTriggerOptions options,
        TimeProvider? clock = null)
    {
        ValidateOptions(options);
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _pump = new MqttSourcePump<MqttReceivedApplicationMessage>(
            options.MaximumPendingMessages,
            RunCoreAsync,
            DisposeCoreAsync);
        Ack = new MqttSignalTarget(HandleSignalAsync, _pump.Completion, MqttWorkflowOutcome.Ack);
        Nak = new MqttSignalTarget(HandleSignalAsync, _pump.Completion, MqttWorkflowOutcome.Nak);
    }

    public ISourceBlock<FlowMessage<MqttReceivedApplicationMessage>> Output => _pump.Output;

    public ISourceBlock<FlowEvent> Events => _pump.Events;

    public IFlowSignalTarget Ack { get; }

    public IFlowSignalTarget Nak { get; }

    public Task Completion => _pump.Completion;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _pump.StartAsync(cancellationToken);

    public void Complete() => _pump.Complete();

    public void Fault(Exception exception) => _pump.Fault(exception);

    public ValueTask DisposeAsync() => _pump.DisposeAsync();

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        _registration = await _controller.RegisterTriggerAsync(new MqttTriggerRegistrationOptions
        {
            TriggerId = _options.TriggerId,
            Subscriptions = _options.Subscriptions,
            WorkflowAcknowledgement = _options.WorkflowAcknowledgement,
            BrokerAcknowledgement = _options.BrokerAcknowledgement,
            MaximumPendingMessages = _options.MaximumPendingMessages
        }, cancellationToken).ConfigureAwait(false);

        await foreach (var delivery in _registration.Messages
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            var envelope = FlowMessage.Create(delivery.Message);
            PendingOutcome? pending = null;
            if (_options.WorkflowAcknowledgement == MqttWorkflowAcknowledgement.Required)
            {
                pending = new PendingOutcome(delivery);
                if (!_pending.TryAdd(envelope.TraceId, pending))
                    throw new InvalidOperationException("MQTT trigger generated a duplicate trace identity.");
                _ = ObserveTimeoutAsync(envelope.TraceId, pending, cancellationToken);
            }

            var accepted = await _pump.EmitAsync(envelope, cancellationToken).ConfigureAwait(false);
            if (!accepted)
            {
                if (pending is not null && _pending.TryRemove(envelope.TraceId, out var removed))
                    removed.Cancel();
                await delivery.CompleteBrokerAcknowledgementAsync(
                    MqttWorkflowOutcome.Nak,
                    CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            _pump.EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = envelope.CorrelationId,
                Name = "mqtt.trigger.received",
                Level = FlowEventLevel.Information,
                Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["client"] = _controller.Name,
                    ["trigger"] = _options.TriggerId,
                    ["topic"] = delivery.Message.Topic,
                    ["traceId"] = envelope.TraceId.Value
                }
            });

            if (_options.BrokerAcknowledgement == MqttBrokerAcknowledgement.AfterHandoff)
            {
                await delivery.CompleteBrokerAcknowledgementAsync(
                    MqttWorkflowOutcome.Ack,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask DisposeCoreAsync()
    {
        foreach (var pending in _pending.Values)
            pending.Cancel();
        _pending.Clear();

        if (_registration is not null)
            await _registration.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask<bool> HandleSignalAsync(
        TraceId traceId,
        MqttWorkflowOutcome outcome,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pending.TryRemove(traceId, out var pending))
        {
            pending.Cancel();
            if (_options.BrokerAcknowledgement == MqttBrokerAcknowledgement.AfterOutcome)
            {
                await pending.Delivery.CompleteBrokerAcknowledgementAsync(
                    outcome,
                    cancellationToken).ConfigureAwait(false);
            }

            EmitOutcomeEvent(traceId, outcome, unknown: false);
            return true;
        }

        EmitOutcomeEvent(traceId, outcome, unknown: true);
        return true;
    }

    private async Task ObserveTimeoutAsync(
        TraceId traceId,
        PendingOutcome pending,
        CancellationToken stopping)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            stopping,
            pending.Cancellation.Token);
        try
        {
            await Task.Delay(_options.OutcomeTimeout, _clock, linked.Token).ConfigureAwait(false);
            if (_pending.TryRemove(traceId, out var removed))
            {
                removed.Cancel();
                if (_options.BrokerAcknowledgement == MqttBrokerAcknowledgement.AfterOutcome)
                {
                    await removed.Delivery.CompleteBrokerAcknowledgementAsync(
                        MqttWorkflowOutcome.Timeout,
                        CancellationToken.None).ConfigureAwait(false);
                }
                EmitOutcomeEvent(traceId, MqttWorkflowOutcome.Timeout, unknown: false);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
    }

    private void EmitOutcomeEvent(TraceId traceId, MqttWorkflowOutcome outcome, bool unknown)
        => _pump.EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = unknown ? "mqtt.trigger.outcome-ignored" : "mqtt.trigger.outcome",
            Level = unknown ? FlowEventLevel.Warning : FlowEventLevel.Information,
            Message = unknown ? "MQTT trigger outcome did not match a pending delivery." : null,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["client"] = _controller.Name,
                ["trigger"] = _options.TriggerId,
                ["traceId"] = traceId.Value,
                ["outcome"] = outcome.ToString()
            }
        });

    private static MqttSubscriptionTriggerOptions ValidateOptions(
        MqttSubscriptionTriggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TriggerId);
        if (options.Subscriptions.Count == 0)
            throw new ArgumentException("An MQTT trigger requires at least one subscription.", nameof(options));
        if (options.MaximumPendingMessages <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumPendingMessages));
        if (options.OutcomeTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.OutcomeTimeout));
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
        return options;
    }

    private sealed class PendingOutcome(MqttTriggerDelivery delivery)
    {
        public MqttTriggerDelivery Delivery { get; } = delivery;

        public CancellationTokenSource Cancellation { get; } = new();

        public void Cancel()
        {
            Cancellation.Cancel();
            Cancellation.Dispose();
        }
    }
}
