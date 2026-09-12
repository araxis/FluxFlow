using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Coordination;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttSubscriptionTriggerNode : FlowSource
{
    private readonly IMqttClientController _controller;
    private readonly MqttSubscriptionTriggerOptions _options;
    private readonly TimeProvider _clock;
    private readonly PendingExchangeCoordinator<TraceId, MqttTriggerDelivery, MqttWorkflowOutcome> _pending;
    private readonly object _observationGate = new();
    private readonly HashSet<Task> _observations = [];
    private IMqttTriggerRegistration? _registration;

    public MqttSubscriptionTriggerNode(
        IMqttClientController controller,
        MqttSubscriptionTriggerOptions options,
        TimeProvider? clock = null)
        : base(CreateSourceOptions(controller, options))
    {
        _controller = controller;
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
        Ack = AddControlInput("Ack", (message, token) => HandleSignalAsync(message.TraceId, MqttWorkflowOutcome.Ack, token), options.MaximumPendingMessages);
        Nak = AddControlInput("Nak", (message, token) => HandleSignalAsync(message.TraceId, MqttWorkflowOutcome.Nak, token), options.MaximumPendingMessages);
    }

    /// <summary>Bounded acknowledgement input. Successful admission does not imply a completed broker outcome.</summary>
    public ITargetBlock<FlowMessage> Ack { get; }

    /// <summary>Bounded rejection input. Ack and Nak have independent queues; the first resolved outcome wins.</summary>
    public ITargetBlock<FlowMessage> Nak { get; }

    protected override async Task RunAsync(CancellationToken cancellationToken)
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
            var envelope = MqttTraceContext.Receive(delivery.Message);
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

            try
            {
                await EmitAsync(envelope, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (tracked)
                    _pending.TryCancel(envelope.TraceId);
                await delivery.CompleteBrokerAcknowledgementAsync(
                    MqttWorkflowOutcome.Nak,
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            EmitEvent(new FlowEvent
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

    protected override async ValueTask OnDisposeAsync()
    {
        List<Exception> failures = [];
        try { _pending.Stop(); }
        catch (Exception error) { failures.Add(error); }

        try { await AwaitObservationsAsync().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }

        try { await _pending.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }

        try
        {
            if (_registration is not null)
                await _registration.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) { failures.Add(error); }

        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException(failures);
    }

    private async Task HandleSignalAsync(
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
            return;
        }

        EmitOutcomeEvent(traceId, outcome, unknown: true);
    }

    private async Task ObserveOutcomeAsync(
        Task<PendingExchangeCompletion<TraceId, MqttTriggerDelivery, MqttWorkflowOutcome>> completionTask)
    {
        try
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
        catch (Exception error)
        {
            // Publish before this observed task ends; disposal must not race a detached fault callback.
            Fault(error);
            throw;
        }
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

                _ = completed.Exception;
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

        var completion = Task.WhenAll(observations);
        try { await completion.ConfigureAwait(false); }
        catch when (completion.Exception?.InnerExceptions.Count > 1)
        {
            throw completion.Exception;
        }
    }

    private void EmitOutcomeEvent(TraceId traceId, MqttWorkflowOutcome outcome, bool unknown)
        => EmitEvent(new FlowEvent
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

    private static FlowSourceOptions CreateSourceOptions(
        IMqttClientController controller, MqttSubscriptionTriggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ValidateOptions(options);
        return new FlowSourceOptions { OutputCapacity = options.MaximumPendingMessages };
    }

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
