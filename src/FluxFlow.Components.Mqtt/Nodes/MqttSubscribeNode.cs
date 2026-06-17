using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Mqtt.Nodes;

public sealed class MqttSubscribeNode : SourceFlowNode<MqttReceivedMessage>, IFlowEventSource, IAsyncDisposable
{
    private const string NotConnectedMessage =
        "MQTT subscribe node is waiting for the mqtt.connection client; establish it (host ConnectAsync) to open the subscription.";

    private readonly object _stateLock = new();
    private readonly IMqttConnectionHandle _connection;
    private readonly MqttSubscriptionOptions _options;
    private readonly TimeProvider _clock;
    private readonly BroadcastBlock<FlowEvent> _events = new(static flowEvent => flowEvent);
    private readonly CancellationTokenSource _lifecycleCancellation = new();

    private Task? _loopTask;
    private bool _started;
    private bool _disposed;

    internal MqttSubscribeNode(
        MqttSubscriptionOptions options,
        IMqttConnectionHandle connection,
        TimeProvider clock)
        : base(new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity })
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ISourceBlock<FlowEvent> Events => _events;

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_started)
            {
                throw new InvalidOperationException("MQTT subscribe node has already started.");
            }

            _started = true;
            _loopTask = RunLoopAsync(_lifecycleCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public override void Complete()
    {
        _lifecycleCancellation.Cancel();
        base.Complete();
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _lifecycleCancellation.Cancel();
        base.Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifecycleCancellation.Cancel();

        Task? loop;
        lock (_stateLock)
        {
            loop = _loopTask;
        }

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        Complete();
        _lifecycleCancellation.Dispose();
    }

    protected override void OnNodeCompleted()
    {
        _events.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        ((IDataflowBlock)_events).Fault(exception);
        base.OnNodeFaulted(exception);
    }

    /// <summary>
    /// Observes the connection through the handle and keeps a single subscription
    /// in step with the established lease. When the connection becomes Connected
    /// with a NEW epoch it (re)subscribes and pumps received messages; when the
    /// connection leaves Connected or a new epoch appears it disposes the prior
    /// subscription first. The epoch dedupes so a within-lease Reconnecting -&gt;
    /// Connected transition on the SAME lease does NOT resubscribe.
    /// </summary>
    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var notConnectedReported = false;
        var subscribedEpoch = -1;
        IMqttSubscription? subscription = null;
        Task? pumpTask = null;
        CancellationTokenSource? pumpCancellation = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Await the next transition BEFORE re-reading state so we never miss
                // a change observed between iterations.
                var change = _connection.WaitForChangeAsync(cancellationToken);

                var state = _connection.State;
                var epoch = _connection.ConnectionEpoch;

                if (state == MqttClientHealthState.Connected &&
                    _connection.TryGetAdapter(out var adapter) &&
                    epoch != subscribedEpoch)
                {
                    // New lease: tear down any prior subscription before resubscribing.
                    await StopPumpAsync(pumpCancellation, pumpTask, subscription).ConfigureAwait(false);
                    pumpCancellation = null;
                    pumpTask = null;
                    subscription = null;

                    try
                    {
                        subscription = await adapter
                            .SubscribeAsync(_options, cancellationToken)
                            .ConfigureAwait(false);
                        ArgumentNullException.ThrowIfNull(subscription);

                        subscribedEpoch = epoch;
                        notConnectedReported = false;

                        TryEmitDiagnostic(
                            MqttDiagnosticNames.SubscribeStarted,
                            message: $"Started MQTT subscription '{_options.TopicFilter}'.",
                            attributes: CreateSubscriptionAttributes());

                        pumpCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        pumpTask = PumpAsync(subscription, pumpCancellation.Token);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
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
                    }
                }
                else if (state != MqttClientHealthState.Connected && subscription is not null)
                {
                    // Connection dropped: stop the pump and dispose the subscription.
                    await StopPumpAsync(pumpCancellation, pumpTask, subscription).ConfigureAwait(false);
                    pumpCancellation = null;
                    pumpTask = null;
                    subscription = null;
                    subscribedEpoch = -1;

                    TryEmitDiagnostic(
                        MqttDiagnosticNames.SubscribeStopped,
                        message: $"Stopped MQTT subscription '{_options.TopicFilter}'.",
                        attributes: CreateSubscriptionAttributes());
                }
                else if (state != MqttClientHealthState.Connected &&
                         subscription is null &&
                         !notConnectedReported)
                {
                    // Informational, once per not-connected stretch: nothing produced
                    // until the host establishes the client.
                    notConnectedReported = true;
                    ReportSubscribeError(
                        MqttErrorCodes.SubscribeNotConnected,
                        NotConnectedMessage);
                    TryEmitDiagnostic(
                        MqttDiagnosticNames.SubscribeFailed,
                        FlowDiagnosticLevel.Information,
                        NotConnectedMessage,
                        attributes: CreateSubscriptionAttributes());
                }

                try
                {
                    await change.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await StopPumpAsync(pumpCancellation, pumpTask, subscription).ConfigureAwait(false);
        }
    }

    private async Task PumpAsync(IMqttSubscription subscription, CancellationToken cancellationToken)
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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
        }
    }

    private static async Task StopPumpAsync(
        CancellationTokenSource? pumpCancellation,
        Task? pumpTask,
        IMqttSubscription? subscription)
    {
        pumpCancellation?.Cancel();

        if (pumpTask is not null)
        {
            try
            {
                await pumpTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Dispose the subscription, never the adapter — that belongs to the
        // connection node.
        if (subscription is not null)
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
        }

        pumpCancellation?.Dispose();
    }

    private void ReportSubscribeError(int code, string message, Exception? exception = null)
        => TryReportError(
            code,
            message,
            exception,
            context: CreateSubscriptionContext());

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
        values.Add($"connectionName={_connection.ConnectionName}");

        return string.Join("; ", values);
    }

    private Dictionary<string, object?> CreateSubscriptionAttributes()
        => new()
        {
            ["topicFilter"] = _options.TopicFilter,
            ["qualityOfService"] = _options.QualityOfService,
            ["receiveRetainedMessages"] = _options.ReceiveRetainedMessages,
            ["retainAsPublished"] = _options.RetainAsPublished,
            ["connectionName"] = _connection.ConnectionName
        };

    private bool EmitReceivedEvent(MqttReceivedMessage message, int payloadBytes)
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
            Timestamp = _clock.GetUtcNow(),
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
