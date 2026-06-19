using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Nodes;

/// <summary>
/// A standalone MQTT subscribe source. Call <c>StartAsync</c> and the node waits for
/// the injected <see cref="IMqttConnectionHandle"/> to establish a client, opens an
/// <see cref="IMqttSubscription"/> on the borrowed adapter, and broadcasts a
/// <c>FlowMessage&lt;MqttReceivedMessage&gt;</c> on <c>Output</c> for each received
/// message (plus notes on <c>Events</c>, failures on <c>Errors</c>). It (re)subscribes
/// on each new connection lease, deduped per connection epoch, and disposes the
/// subscription — never the adapter — when the connection drops or the node stops. The
/// node never connects or disposes the client. Works with nothing but a connection
/// handle — no engine.
/// </summary>
public sealed class MqttSubscribeNode : FlowSource<MqttReceivedMessage>
{
    private const string NotConnectedMessage =
        "MQTT subscribe node is waiting for the mqtt.connection client; establish it (host ConnectAsync) to open the subscription.";

    private readonly IMqttConnectionHandle _connection;
    private readonly MqttSubscriptionOptions _options;
    private readonly TimeProvider _clock;

    public MqttSubscribeNode(
        IMqttConnectionHandle connection,
        MqttSubscriptionOptions? options = null,
        TimeProvider? clock = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? new MqttSubscriptionOptions();
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Observes the connection through the handle and keeps a single subscription
    /// in step with the established lease. When the connection becomes Connected
    /// with a NEW epoch it (re)subscribes and pumps received messages; when the
    /// connection leaves Connected or a new epoch appears it disposes the prior
    /// subscription first. The epoch dedupes so a within-lease Reconnecting -&gt;
    /// Connected transition on the SAME lease does NOT resubscribe.
    /// </summary>
    protected override async Task RunAsync(CancellationToken cancellationToken)
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

                        EmitEvent(new FlowEvent
                        {
                            Timestamp = _clock.GetUtcNow(),
                            Name = MqttEventNames.SubscribeStarted,
                            Level = FlowEventLevel.Information,
                            Message = $"Started MQTT subscription '{_options.TopicFilter}'.",
                            Attributes = CreateSubscriptionAttributes()
                        });

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

                    EmitEvent(new FlowEvent
                    {
                        Timestamp = _clock.GetUtcNow(),
                        Name = MqttEventNames.SubscribeStopped,
                        Level = FlowEventLevel.Information,
                        Message = $"Stopped MQTT subscription '{_options.TopicFilter}'.",
                        Attributes = CreateSubscriptionAttributes()
                    });
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
                        NotConnectedMessage,
                        level: FlowEventLevel.Information);
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

                EmitEvent(new FlowEvent
                {
                    Timestamp = _clock.GetUtcNow(),
                    CorrelationId = ToCorrelationId(message.CorrelationId),
                    Name = MqttEventNames.SubscribeReceived,
                    Level = FlowEventLevel.Information,
                    Message = $"Received MQTT message from '{message.Topic}'.",
                    Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["topic"] = message.Topic,
                        ["payloadBytes"] = payloadBytes,
                        ["qualityOfService"] = message.QualityOfService.ToString(),
                        ["retain"] = message.Retain,
                        ["correlationId"] = message.CorrelationId,
                        ["connectionName"] = _connection.ConnectionName
                    }
                });

                // Mint the first envelope of the inbound exchange, flowing the
                // adapter-supplied correlation id when present.
                Emit(FlowMessage.Create(message, ToCorrelationId(message.CorrelationId)));
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

    private static CorrelationId? ToCorrelationId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new CorrelationId(value);

    private void ReportSubscribeError(
        int code,
        string message,
        Exception? exception = null,
        FlowEventLevel level = FlowEventLevel.Error)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            Code = code,
            Message = message,
            Context = CreateSubscriptionContext(),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = MqttEventNames.SubscribeFailed,
            Level = level,
            Message = message,
            Attributes = CreateSubscriptionAttributes()
        });
    }

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
        => new(StringComparer.Ordinal)
        {
            ["topicFilter"] = _options.TopicFilter,
            ["qualityOfService"] = _options.QualityOfService.ToString(),
            ["receiveRetainedMessages"] = _options.ReceiveRetainedMessages,
            ["retainAsPublished"] = _options.RetainAsPublished,
            ["connectionName"] = _connection.ConnectionName
        };
}
