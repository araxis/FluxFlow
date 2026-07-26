using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Coordination;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttSubscriptionTriggerNode : IFlowSource
{
    private readonly IMqttClientController _controller;
    private readonly MqttSubscriptionTriggerOptions _options;
    private readonly TimeProvider _clock;
    private readonly PendingExchangeCoordinator<TraceId, MqttTriggerDelivery, MqttWorkflowOutcome> _pending;
    private readonly object _observationGate = new();
    private readonly HashSet<Task> _observations = [];
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
        _pending = new PendingExchangeCoordinator<TraceId, MqttTriggerDelivery, MqttWorkflowOutcome>(
            new PendingExchangeCoordinatorOptions
            {
                DefaultTimeout = options.OutcomeTimeout,
                MaxPending = options.MaximumPendingMessages,
                SettledKeyCapacity = Math.Max(options.MaximumPendingMessages, 4096)
            },
            _clock);
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
            var tracked = false;
            if (_options.WorkflowAcknowledgement == MqttWorkflowAcknowledgement.Required)
            {
                var started = _pending.TryStart(envelope.TraceId, delivery);
                if (!started.IsAccepted)
                {
                    throw new InvalidOperationException(
                        $"MQTT workflow acknowledgement could not start for trace '{envelope.TraceId}' ({started.Status}).");
                }

                tracked = true;
                TrackObservation(ObserveOutcomeAsync(started.Completion!));
            }

            var accepted = await _pump.EmitAsync(envelope, cancellationToken).ConfigureAwait(false);
            if (!accepted)
            {
                if (tracked)
                    _pending.TryCancel(envelope.TraceId);
                await delivery.CompleteBrokerAcknowledgementAsync(
                    MqttWorkflowOutcome.Nak,
                    CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            _pump.EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = envelope.CorrelationId,
                Name = "mqtt.receive.received",
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
        _pending.Stop();
        await AwaitObservationsAsync().ConfigureAwait(false);
        await _pending.DisposeAsync().ConfigureAwait(false);

        if (_registration is not null)
            await _registration.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask<bool> HandleSignalAsync(
        TraceId traceId,
        MqttWorkflowOutcome outcome,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var feedback = _pending.TryResolve(traceId, outcome);
        if (feedback.IsResolved)
        {
            if (_options.BrokerAcknowledgement == MqttBrokerAcknowledgement.AfterOutcome)
            {
                await feedback.Completion!.Context.CompleteBrokerAcknowledgementAsync(
                    outcome,
                    cancellationToken).ConfigureAwait(false);
            }

            EmitOutcomeEvent(traceId, outcome, unknown: false);
            return true;
        }

        EmitOutcomeEvent(traceId, outcome, unknown: true);
        return true;
    }

    private async Task ObserveOutcomeAsync(
        Task<PendingExchangeCompletion<TraceId, MqttTriggerDelivery, MqttWorkflowOutcome>> completionTask)
    {
        var completion = await completionTask.ConfigureAwait(false);
        if (completion.Kind != PendingExchangeCompletionKind.TimedOut)
            return;

        if (_options.BrokerAcknowledgement == MqttBrokerAcknowledgement.AfterOutcome)
        {
            await completion.Context.CompleteBrokerAcknowledgementAsync(
                MqttWorkflowOutcome.Timeout,
                CancellationToken.None).ConfigureAwait(false);
        }

        EmitOutcomeEvent(completion.Key, MqttWorkflowOutcome.Timeout, unknown: false);
    }

    private void TrackObservation(Task observation)
    {
        lock (_observationGate)
            _observations.Add(observation);

        _ = observation.ContinueWith(
            completed =>
            {
                lock (_observationGate)
                    _observations.Remove(completed);

                if (completed.IsFaulted)
                    _pump.Fault(completed.Exception!.GetBaseException());
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async ValueTask AwaitObservationsAsync()
    {
        Task[] observations;
        lock (_observationGate)
            observations = _observations.ToArray();

        if (observations.Length > 0)
            await Task.WhenAll(observations).ConfigureAwait(false);
    }

    private void EmitOutcomeEvent(TraceId traceId, MqttWorkflowOutcome outcome, bool unknown)
        => _pump.EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = unknown ? "mqtt.receive.outcome-ignored" : "mqtt.receive.outcome",
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

}
