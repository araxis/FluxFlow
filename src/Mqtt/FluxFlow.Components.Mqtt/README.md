# FluxFlow.Components.Mqtt

Standalone MQTT nodes for FluxFlow, built on the [FluxFlow.Nodes](../../FluxFlow.Nodes/README.md)
kit. No engine, registry, runtime, or concrete network client is required: provide a
publisher or trigger-source implementation in the host, `new` the nodes, `LinkTo`
their ports, and run.

Applications supply small interfaces that can be implemented over any MQTT client
library:

- `IMqttPublisher` publishes `MqttPublishRequest` values.
- `IMqttTriggerSource` opens `IMqttSubscription` streams for trigger nodes.
- `IMqttClientHealthSource` exposes optional client-health transitions.

One concrete object may implement any combination of these interfaces. The MQTT nodes
depend only on the role they actually need.

## Nodes

| Node | Shape | Purpose |
|------|-------|---------|
| `MqttPublishNode` | `FlowNode<MqttPublishRequest, MqttPublishResult>` | Publishes a request through an injected `IMqttPublisher` and emits a result carrying the same correlation id. |
| `MqttTriggerNode` | `FlowSource<MqttReceivedMessage>` | Opens one subscription through an injected `IMqttTriggerSource` and emits each received message. In request/reply mode it waits for a correlated `MqttTriggerResponse`. |

Publish and trigger are ordinary kit nodes: a bounded `Input` for publish,
broadcast `Output`, `Errors`, and `Events` ports, plus a `Responses` target on
`MqttTriggerNode` when request/reply acknowledgement is needed. Nodes never create,
start, stop, reconnect, or dispose a concrete client. Client-session ownership,
connection policy, and reconnect policy belong behind the supplied interfaces.

## Contracts

```csharp
public interface IMqttPublisher
{
    ValueTask PublishAsync(MqttPublishRequest request, CancellationToken cancellationToken = default);
}

public interface IMqttTriggerSource
{
    ValueTask<IMqttSubscription> SubscribeAsync(
        MqttTriggerOptions options,
        CancellationToken cancellationToken = default);
}

public interface IMqttSubscription : IAsyncDisposable
{
    IAsyncEnumerable<IMqttReceivedContext> Messages { get; }
}

public interface IMqttReceivedContext
{
    MqttReceivedMessage Message { get; }
    ValueTask AckAsync(CancellationToken cancellationToken = default);
    ValueTask NackAsync(Exception? error = null, CancellationToken cancellationToken = default);
}

public interface IMqttClientHealthSource
{
    IAsyncEnumerable<MqttClientHealthEvent> Health { get; }
}
```

Implementations should throw `MqttClientUnavailableException` when they cannot publish
or open a trigger subscription because no client is currently available. Nodes
translate that into the package not-connected error codes and keep running.

## Publish

```csharp
var publish = new MqttPublishNode(
    publisher,
    new MqttPublishOptions
    {
        PublishTimeoutMilliseconds = 30_000,
        BoundedCapacity = 128
    });

publish.Output.LinkTo(resultSink);
await publish.Input.SendAsync(FlowMessage.Create(new MqttPublishRequest
{
    Topic = "devices/temperature",
    Payload = payloadBytes,
    QualityOfService = MqttQualityOfService.AtLeastOnce,
    Retain = false,
    Properties = new MqttPublishProperties
    {
        CorrelationId = "abc"
    }
}));
```

The result carries the inbound `FlowMessage` correlation id forward. When the
publisher reports unavailability, the request reports
`MqttErrorCodes.PublishNotConnected` on `Errors` and the node continues with later
requests. Each publish is bounded by `publishTimeoutMilliseconds` (default `30000`),
so a hung implementation cannot wedge the node.

Each `MqttPublishRequest` carries its publish `Topic` explicitly. The publish node
does not fill a missing topic from static options. Quality of service and retain
are also request-owned publish semantics; static publish options only control node
runtime behavior such as timeout and bounded input capacity.

`MqttPublishRequest.Properties` contains MQTT protocol metadata such as MQTT
correlation id, response topic, and user properties. Workflow correlation stays on
the surrounding `FlowMessage`.

## Trigger

```csharp
var trigger = new MqttTriggerNode(
    triggerSource,
    new MqttTriggerOptions { TopicFilter = "devices/+/state" });

trigger.Output.LinkTo(messageSink);
await trigger.StartAsync();
```

The trigger source opens one `IMqttSubscription` and emits a
`FlowMessage<MqttReceivedMessage>` for each received message, flowing the
implementation-supplied correlation id when present. The subscription is disposed
when the source stops. Reconnect and resubscribe behavior belongs inside the supplied
`IMqttTriggerSource` or concrete client adapter.

For request/reply handling, configure the trigger and post the graph response back to
`Responses` with the same correlation id:

```csharp
var trigger = new MqttTriggerNode(
    triggerSource,
    new MqttTriggerOptions
    {
        TopicFilter = "commands/+",
        Mode = MqttTriggerMode.RequestReply,
        Acknowledgement = MqttTriggerAcknowledgement.OnSuccessfulResponse,
        ResponseTimeout = TimeSpan.FromSeconds(30)
    });

trigger.Output.LinkTo(handler);

await trigger.Responses.SendAsync(receivedMessage.With(
    MqttTriggerResponse.Success()));
```

`MqttTriggerAcknowledgement.None` leaves ack/nack entirely to the adapter. `OnEmit`
acknowledges after the trigger emits the message to `Output`. `OnSuccessfulResponse`
is for request/reply mode: success responses call `AckAsync`; failure responses and
timeouts call `NackAsync`.

Request/reply correlation, duplicate detection, timeout, and pending cleanup use the
shared `CorrelatedRequestTracker` from `FluxFlow.Components.RequestReply`; MQTT keeps
only MQTT-specific subscription and ack/nack policy in the trigger node.

## Adapter-Owned Client Session

Broker addresses, credentials, reconnect policy, concrete client lifetime, and MQTT
Last Will belong to the supplied implementation or a future adapter package. Last
Will is registered during MQTT `CONNECT`, so it is not part of
`MqttPublishOptions` or `MqttTriggerOptions`. For graceful offline messages, publish
an ordinary `MqttPublishRequest`.

## Client Health

Adapters that also implement `IMqttClientHealthSource` can expose connection health
transitions to hosts, dashboards, or future monitoring nodes. The publish and trigger
nodes do not consume health directly.

Incoming `MqttReceivedMessage.Timestamp` and adapter-provided
`MqttClientHealthEvent.Timestamp` values stay adapter-owned.

## Runtime Timing

Pass a `TimeProvider` to any node when tests or hosts need deterministic package-owned
timestamps such as publish result times, node event times, and trigger response
timeouts.

## Topic Validation

Use `MqttTopicValidator.ValidatePublishTopic` and
`MqttTopicValidator.ValidateSubscriptionFilter` when projecting host settings or
building requests. Publish request topics must be present and cannot contain MQTT wildcards.
Subscription filters may use `+` as a complete level and `#` only as the final
complete level. Both helpers reject null characters and oversized encoded topics.
`MqttPublishNode` validates publish request topics before calling `IMqttPublisher`;
`MqttTriggerNode` validates its static `TopicFilter` before opening a subscription.
