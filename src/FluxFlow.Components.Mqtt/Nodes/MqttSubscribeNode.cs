using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Timing;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttSubscribeNode : SourceFlowNode<MqttReceivedMessage>, IFlowEventSource, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly IMqttClientFactory _clientFactory;
    private readonly MqttClientFactoryContext _factoryContext;
    private readonly MqttSubscriptionOptions _options;
    private readonly IMqttClock _clock;
    private readonly BroadcastBlock<FlowEvent> _events = new(static flowEvent => flowEvent);
    private CancellationTokenSource? _subscriptionCancellation;
    private Task? _subscriptionTask;
    private MqttHealthMonitor? _healthMonitor;
    private MqttClientLease? _clientLease;
    private IMqttSubscription? _subscription;
    private bool _started;
    private bool _completed;
    private bool _disposed;

    private MqttSubscribeNode(
        MqttSubscriptionOptions options,
        MqttClientFactoryContext factoryContext,
        IMqttClientFactory clientFactory,
        IMqttClock clock)
        : base(new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity })
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _factoryContext = factoryContext ?? throw new ArgumentNullException(nameof(factoryContext));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ISourceBlock<FlowEvent> Events => _events;

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        IMqttClientFactory clientFactory,
        IMqttClock clock)
    {
        var options = MqttOptionsReader.ReadSubscriptionOptions(context.Definition);
        var node = new MqttSubscribeNode(
            options,
            MqttClientFactoryContexts.Create(context, options, clock),
            clientFactory,
            clock);

        return context.CreateNode(node)
            .Output(MqttComponentPorts.Output, node.Output)
            .Build();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_started)
            {
                throw new InvalidOperationException("MQTT subscribe node has already started.");
            }

            if (_completed)
            {
                return;
            }

            _started = true;
        }

        MqttClientLease? clientLease = null;
        try
        {
            clientLease = await _clientFactory.CreateAsync(_factoryContext, cancellationToken)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(clientLease);

            var subscription = await clientLease.Adapter.SubscribeAsync(_options, cancellationToken)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(subscription);

            var subscriptionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            TryEmitDiagnostic(
                MqttDiagnosticNames.SubscribeStarted,
                message: $"Started MQTT subscription '{_options.TopicFilter}'.",
                attributes: CreateSubscriptionAttributes());

            lock (_stateLock)
            {
                _clientLease = clientLease;
                _subscription = subscription;
                _subscriptionCancellation = subscriptionCancellation;
                _subscriptionTask = RunSubscriptionAsync(subscription, subscriptionCancellation.Token);
                if (_completed)
                {
                    subscriptionCancellation.Cancel();
                }
            }

            StartHealthMonitor(clientLease.Adapter);
        }
        catch (OperationCanceledException)
        {
            if (clientLease is not null)
            {
                await clientLease.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception exception)
        {
            if (clientLease is not null)
            {
                await clientLease.DisposeAsync().ConfigureAwait(false);
            }

            ReportSubscribeError(
                MqttErrorCodes.SubscribeStartupFailed,
                $"MQTT subscribe startup failed: {exception.Message}",
                exception);
            TryEmitDiagnostic(
                MqttDiagnosticNames.SubscribeFailed,
                FlowDiagnosticLevel.Error,
                $"MQTT subscribe startup failed for '{_options.TopicFilter}'.",
                exception,
                CreateSubscriptionAttributes());

            throw new InvalidOperationException(
                $"MQTT subscribe node failed to start for '{_options.TopicFilter}'.",
                exception);
        }
    }

    public override void Complete()
    {
        lock (_stateLock)
        {
            _completed = true;
            if (_subscriptionCancellation is null)
            {
                base.Complete();
                return;
            }

            _subscriptionCancellation.Cancel();
        }
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _subscriptionCancellation?.Cancel();
        base.Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscriptionCancellation?.Cancel();
        await StopHealthMonitorAsync().ConfigureAwait(false);

        if (_subscription is not null)
        {
            await _subscription.DisposeAsync().ConfigureAwait(false);
        }

        if (_subscriptionTask is not null)
        {
            await _subscriptionTask.ConfigureAwait(false);
        }

        _subscriptionCancellation?.Dispose();

        if (_clientLease is not null)
        {
            await _clientLease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RunSubscriptionAsync(
        IMqttSubscription subscription,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in subscription.Messages
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                var payloadBytes = message.Payload?.Length ?? 0;

                TryEmitDiagnostic(
                    MqttDiagnosticNames.SubscribeReceived,
                    message: $"Received MQTT message from '{message.Topic}'.",
                    attributes: new Dictionary<string, object?>
                    {
                        ["topic"] = message.Topic,
                        ["payloadBytes"] = payloadBytes,
                        ["qualityOfService"] = message.QualityOfService,
                        ["retain"] = message.Retain,
                        ["correlationId"] = message.CorrelationId
                    });
                EmitReceivedEvent(message, payloadBytes);

                if (!await SendOutputAsync(message, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }

            TryEmitDiagnostic(
                MqttDiagnosticNames.SubscribeStopped,
                message: $"Stopped MQTT subscription '{_options.TopicFilter}'.",
                attributes: CreateSubscriptionAttributes());
            CompleteOutput();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryEmitDiagnostic(
                MqttDiagnosticNames.SubscribeStopped,
                message: $"Stopped MQTT subscription '{_options.TopicFilter}'.",
                attributes: CreateSubscriptionAttributes());
            CompleteOutput();
        }
        catch (Exception exception)
        {
            ReportSubscribeError(
                MqttErrorCodes.SubscribeFailed,
                $"MQTT subscribe failed: {exception.Message}",
                exception);
            TryEmitDiagnostic(
                MqttDiagnosticNames.SubscribeFailed,
                FlowDiagnosticLevel.Error,
                $"MQTT subscribe failed for '{_options.TopicFilter}'.",
                exception,
                CreateSubscriptionAttributes());
            base.Fault(exception);
        }
    }

    protected override void OnNodeCompleted()
    {
        CancelHealthMonitor();
        _events.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        CancelHealthMonitor();
        ((IDataflowBlock)_events).Fault(exception);
        base.OnNodeFaulted(exception);
    }

    private void StartHealthMonitor(IMqttClientAdapter adapter)
        => _healthMonitor = MqttHealthMonitor.Start(adapter, _clock, EmitHealth, EmitHealthFailure);

    private void CancelHealthMonitor()
        => _healthMonitor?.Cancel();

    private async ValueTask StopHealthMonitorAsync()
    {
        if (_healthMonitor is not null)
        {
            await _healthMonitor.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void EmitHealthFailure(MqttClientHealthEvent health, Exception exception)
    {
        TryEmitDiagnostic(
            MqttDiagnosticNames.ConnectionHealthChanged,
            FlowDiagnosticLevel.Error,
            "MQTT connection health stream failed.",
            exception,
            MqttHealthSignal.CreateDiagnosticAttributes(
                health,
                _factoryContext.ConnectionName));
        EmitHealthEvent(health);
    }

    private void EmitHealth(MqttClientHealthEvent health)
    {
        TryEmitDiagnostic(
            MqttDiagnosticNames.ConnectionHealthChanged,
            MqttHealthSignal.GetLevel(health),
            MqttHealthSignal.CreateMessage(health),
            attributes: MqttHealthSignal.CreateDiagnosticAttributes(
                health,
                _factoryContext.ConnectionName));
        EmitHealthEvent(health);
    }

    private bool EmitHealthEvent(MqttClientHealthEvent health)
        => _events.Post(new FlowEvent
        {
            Timestamp = _clock.UtcNow,
            Type = MqttEventNames.ConnectionHealthChanged,
            Source = Id.ToString(),
            SourceNodeId = Id,
            Subject = MqttHealthSignal.CreateSubject(health, _factoryContext.ConnectionName),
            Status = health.State.ToString(),
            Channel = MqttEventNames.ConnectionHealthChanged,
            Attributes = MqttHealthSignal.CreateEventAttributes(
                health,
                _factoryContext.ConnectionName)
        });

    private void ReportSubscribeError(
        int code,
        string message,
        Exception exception)
        => TryReportError(
            code,
            message,
            exception,
            CreateSubscriptionContext());

    private string CreateSubscriptionContext()
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(_options.TopicFilter))
        {
            values.Add($"topicFilter={_options.TopicFilter}");
        }

        values.Add($"qualityOfService={_options.QualityOfService}");
        values.Add($"receiveRetainedMessages={_options.ReceiveRetainedMessages}");
        values.Add($"retainAsPublished={_options.RetainAsPublished}");

        if (!string.IsNullOrWhiteSpace(_factoryContext.ConnectionName))
        {
            values.Add($"connectionName={_factoryContext.ConnectionName}");
        }

        return string.Join("; ", values);
    }

    private Dictionary<string, object?> CreateSubscriptionAttributes()
    {
        var attributes = new Dictionary<string, object?>
        {
            ["topicFilter"] = _options.TopicFilter,
            ["qualityOfService"] = _options.QualityOfService,
            ["receiveRetainedMessages"] = _options.ReceiveRetainedMessages,
            ["retainAsPublished"] = _options.RetainAsPublished
        };

        if (!string.IsNullOrWhiteSpace(_factoryContext.ConnectionName))
        {
            attributes["connectionName"] = _factoryContext.ConnectionName;
        }

        return attributes;
    }

    private bool EmitReceivedEvent(
        MqttReceivedMessage message,
        int payloadBytes)
    {
        var attributes = new Dictionary<string, string>
        {
            ["payloadBytes"] = payloadBytes.ToString(),
            ["qualityOfService"] = message.QualityOfService.ToString(),
            ["retain"] = message.Retain.ToString()
        };

        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
        {
            attributes["correlationId"] = message.CorrelationId;
        }

        return _events.Post(new FlowEvent
        {
            Timestamp = _clock.UtcNow,
            Type = MqttEventNames.SubscribeReceived,
            Source = Id.ToString(),
            SourceNodeId = Id,
            Subject = message.Topic,
            Channel = MqttEventNames.SubscribeReceived,
            PayloadBytes = payloadBytes,
            PayloadPreview = message.PayloadPreview,
            Attributes = attributes
        });
    }
}
