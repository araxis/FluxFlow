# FluxFlow.Components.Mqtt

Transport-neutral MQTT client orchestration and standalone components for
FluxFlow. The 5.x core uses [FluxFlow.Data](../../FluxFlow.Data/README.md)
`FlowContent`, normal polymorphic command results, and one host-lifetime client
controller per logical MQTT client. It does not depend on Engine, Composition,
or a concrete MQTT client library.

The previous 4.x publish/trigger contracts remain in this package temporarily
so existing adapters continue to build while they migrate through the shared
transport conformance suite. New applications should use the 5.x contracts
described first below.

## 5.x Ownership Model

- `MqttBrokerConfiguration` describes one broker endpoint, port, TLS transport,
  and server name. It does not own a client session.
- `MqttClientConfiguration` describes one logical client identity, resolved
  credentials/certificates, clean start, keepalive, Last Will, auto-connect,
  reconnect policy, and initial named subscriptions.
- Each `MqttClientController` owns exactly one neutral transport session and may
  share its broker configuration with other independent controllers.
- `IMqttTransportFactory` and `IMqttTransportSession` are the adapter SPI.
  Concrete client-library types never cross that boundary.
- The host owns the controller lifetime. Control, publish, trigger, and events
  components share it and never dispose it.

Credentials and certificates in `MqttClientConfiguration` are already resolved
host values. Configuration binding applies deployment override, direct client
value, referenced resource, then default precedence before constructing the
controller. Inline secret policy also belongs to the host, not this runtime
contract.

## 5.x Components

| Component class | Canonical type | Shape | Purpose |
|---|---|---|---|
| `MqttControlNode` | `mqtt.command` | `Input` -> `Output` | Executes Connect, Disconnect, Status, Publish, Subscribe, and Unsubscribe requests and emits exactly one `MqttClientResult` for every accepted request. |
| `MqttPublishOperationNode` | `mqtt.publish` | `Input` -> `Output` | Focused convenience component over the same publish request/result path. |
| `MqttSubscriptionTriggerNode` | `mqtt.receive` | `Output`, `Ack`, `Nak` | Emits received `FlowContent` messages and accepts payload-independent workflow outcome signals matched by `TraceId`. |
| `MqttClientEventsNode` | `mqtt.events` | `Output` | Emits reliable connection, subscription, and reconnect domain events for workflow use. |

The new components expose no universal `Errors` or `State` port. Expected
failures are `MqttClientFailureResult` values on normal `Output`. Component
diagnostics remain on `Events`; an unexpected component fault is surfaced by
component completion and the Engine system-event stream when hosted.

`MqttControlOptions` uses semantic scheduling settings:

- `RequestProcessing`: `Sequential` or `Concurrent`.
- `ResultOrder`: `PreserveInput` or `Completion`.
- `MaximumConcurrentRequests` and `MaximumPendingRequests`.

Lifecycle and subscription mutations are serialized inside the controller.
Publish and status operations may run concurrently. Explicit Disconnect
suppresses reconnect until Connect or host restart. Disconnected operations
return immediate transient result errors.

## Standalone C#

```csharp
var controller = new MqttClientController(
    new MqttClientConfiguration
    {
        Name = "Resources.Messaging.TelemetryClient",
        ClientId = "telemetry-client",
        Broker = new MqttBrokerConfiguration
        {
            Host = "broker.internal",
            Port = 8883,
            UseTls = true
        },
        AutoConnect = MqttAutoConnectMode.OnStart,
        Reconnect = new MqttReconnectConfiguration
        {
            Enabled = true,
            Policy = new MqttRetryPolicy
            {
                Strategy = MqttRetryStrategy.Exponential,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaximumDelay = TimeSpan.FromMinutes(1),
                JitterFactor = 0.2
            }
        }
    },
    transportFactory);

await controller.StartAsync();

await using var control = new MqttControlNode(
    controller,
    new MqttControlOptions
    {
        RequestProcessing = MqttRequestProcessing.Concurrent,
        ResultOrder = MqttResultOrder.PreserveInput,
        MaximumConcurrentRequests = 8,
        MaximumPendingRequests = 128
    });

await control.Input.SendAsync(FlowMessage.Create<MqttClientRequest>(
    new MqttPublishClientRequest
    {
        Message = new MqttPublishMessage
        {
            Topic = "telemetry/line-1",
            Content = FlowContent.FromBytes(payload, "application/json"),
            Qos = MqttQos.AtLeastOnce
        }
    }));
```

The request JSON discriminator is `Operation`. Result JSON uses `Kind`, has a
computed `IsError`, and carries workflow-friendly `FlowError` details. The
focused publish component uses the same controller path and result variants.

## Subscriptions And Acknowledgements

A trigger accepts one or many `MqttSubscriptionTarget` values. A named target
is created with `MqttSubscriptionTarget.Named(...)`; an inline target uses
`FromInline(...)`. Named subscriptions are client-owned and survive trigger
removal. Inline subscriptions are trigger-owned and are removed with the
trigger.

Missing named subscriptions leave the trigger waiting. A successful Subscribe
command creates or updates the named desired subscription and activates waiting
triggers. Desired subscriptions are restored after reconnect. Unsubscribe
removes named desired state. One trigger may claim a subscription identity;
identical filters cannot be claimed by different triggers, while different
overlapping filters are valid. One publication matching several subscriptions
inside one trigger is emitted once with all `MatchedSubscriptions`.

Workflow acknowledgement and broker acknowledgement are separate:

- `MqttWorkflowAcknowledgement.None` or `Required` controls Ack/Nak signals.
- `MqttBrokerAcknowledgement.Automatic` is independent of workflow outcome.
- `AfterHandoff` completes broker acknowledgement after output acceptance.
- `AfterOutcome` maps Ack, Nak, or timeout through adapter capabilities.
- QoS 0 never performs broker acknowledgement.

Ack and Nak ignore signal payload type. The first signal matching a pending
delivery `TraceId` wins; duplicate, conflicting, late, and unknown signals are
diagnostic events only. Deferred policies are rejected during trigger
registration when the adapter does not advertise the required capability.

## Composition

### Canonical JSON Target

The vNext composition migration will bind the 5.x core from the canonical flat
document below. The current `FluxFlow.Components.Mqtt.Composition` package still
binds the legacy 4.x publish/trigger surface until the adapter/conformance
milestone is complete.

```json
{
  "Resources": {
    "Messaging": {
      "Broker1": {
        "Type": "mqtt.broker",
        "Host": "broker.internal",
        "Port": 8883,
        "UseTls": true
      },
      "TelemetryClient": {
        "Type": "mqtt.client",
        "Broker": "Resources.Messaging.Broker1",
        "ClientId": "telemetry-client",
        "AutoConnect": "OnStart",
        "Reconnect": "Resources.Resilience.MqttRetry"
      },
      "Commands": {
        "Type": "mqtt.subscription",
        "TopicFilter": "commands/+",
        "Qos": "AtLeastOnce"
      }
    },
    "Resilience": {
      "MqttRetry": {
        "Type": "retry.policy",
        "Strategy": "Exponential",
        "InitialDelay": "00:00:01",
        "MaximumDelay": "00:01:00",
        "JitterFactor": 0.2
      }
    }
  },
  "Workflows": {
    "OrderProcessing": {
      "ReceiveCommands": {
        "Type": "mqtt.receive",
        "Client": "Resources.Messaging.TelemetryClient",
        "Subscription": "Resources.Messaging.Commands",
        "WorkflowAcknowledgement": "Required",
        "BrokerAcknowledgement": "AfterOutcome",
        "Output": "HandleCommand.Input"
      },
      "HandleCommand": {
        "Type": "orders.handle-command",
        "Accepted": "ReceiveCommands.Ack",
        "Rejected": "ReceiveCommands.Nak"
      }
    }
  }
}
```

One subscription uses a string or inline object directly; multiple
subscriptions use a mixed array of those forms. `Qos` is the canonical property
name. Components remain flat: there is no per-node Resources, Nodes, Links, or
Composition wrapper.

## Legacy 4.x Contracts

The declarations below are retained only for coordinated adapter and
composition migration.

Applications supply small interfaces that can be implemented over any MQTT client
library:

- `IMqttPublisher` publishes `MqttPublishRequest` values.
- `IMqttTriggerSource` opens `IMqttSubscription` streams for trigger nodes.
- `IMqttClientHealthSource` exposes optional client-health transitions.

One concrete object may implement any combination of these interfaces. The MQTT nodes
depend only on the role they actually need.

### Legacy Nodes

| Node | Shape | Purpose |
|------|-------|---------|
| `MqttPublishNode` | `FlowNode<MqttPublishRequest, MqttPublishResult>` | Publishes a request through an injected `IMqttPublisher` and emits a result carrying the same correlation id. |
| `MqttTriggerNode` | `FlowSource<MqttReceivedMessage>` | Opens one subscription through an injected `IMqttTriggerSource` and emits each received message. In request/reply mode it waits for a correlated `MqttTriggerResponse`. |

Publish and trigger are ordinary kit nodes: a bounded `Input` for publish,
broadcast `Output`, `Errors`, and `Events` ports, plus a `Responses` target on
`MqttTriggerNode` when request/reply acknowledgement is needed. Nodes never create,
start, stop, reconnect, or dispose a concrete client. Client-session ownership,
connection policy, and reconnect policy belong behind the supplied interfaces.

### Legacy Contracts

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

### Legacy Publish

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
User-property dictionaries are snapshotted when assigned, and null maps are
treated as empty so caller-owned mutable dictionaries cannot alter queued
publish contracts after creation.
Publish payload byte arrays are also copied when assigned, so mutating the
caller-owned buffer after creating a request does not change the request.

### Legacy Trigger

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
Malformed received contexts from an adapter are reported on `Errors` and do not
stop later valid subscription messages from flowing.

`MqttTriggerOptions.BoundedCapacity` configures bounded broadcast source output.
Trigger receive processing awaits output-block acceptance before `OnEmit`
acknowledgement, while output still follows the kit's broadcast/latest-wins
semantics. In request/reply mode, the same capacity also bounds the `Responses`
target.

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

### Legacy Adapter-Owned Client Session

Broker addresses, credentials, reconnect policy, concrete client lifetime, and MQTT
Last Will belong to the supplied implementation or a future adapter package. Last
Will is registered during MQTT `CONNECT`, so it is not part of
`MqttPublishOptions` or `MqttTriggerOptions`. For graceful offline messages, publish
an ordinary `MqttPublishRequest`.

### Legacy Client Health

Adapters that also implement `IMqttClientHealthSource` can expose connection health
transitions to hosts, dashboards, or future monitoring nodes. The publish and trigger
nodes do not consume health directly.

Incoming `MqttReceivedMessage.Timestamp` and adapter-provided
`MqttClientHealthEvent.Timestamp` values stay adapter-owned.
Received-message user properties and health-event attributes are snapshotted
when assigned, matching the immutable-envelope behavior used across the node
contract surface.
Received payload and correlation-data byte arrays are copied when assigned so
adapter-owned buffers cannot mutate a received-message contract after mapping.

### Legacy Runtime Timing

Pass a `TimeProvider` to any node when tests or hosts need deterministic package-owned
timestamps such as publish result times, node event times, and trigger response
timeouts.

### Legacy Composition Support

Add `FluxFlow.Components.Mqtt.Composition` when a host wants to instantiate MQTT
nodes from `FluxFlow.Composition` fluent definitions or `IConfiguration` JSON.
That optional package registers explicit `mqtt.publish` and `mqtt.receive`
factories while this core package stays focused on standalone nodes and neutral
MQTT contracts.

Composition node factories resolve host-owned adapter resources by key:

- `publisher` maps to a keyed `IMqttPublisher`.
- `triggerSource` maps to a keyed `IMqttTriggerSource`.
- `clock` optionally maps to a keyed `TimeProvider`.

Concrete adapter packages or the host still own broker settings, credentials,
connection lifetime, reconnect behavior, and any client-specific features. The
composition package only consumes those resources; it does not create,
connect, reconnect, or dispose MQTT clients.

The optional composition package also exposes
`MqttComponentDesignMetadataProvider` for neutral Designer metadata over the
`mqtt.publish` and `mqtt.receive` composition node types. The standalone MQTT
package remains free of Designer, Composition, and Engine dependencies.

### Topic Validation

Use `MqttTopicValidator.ValidatePublishTopic` and
`MqttTopicValidator.ValidateSubscriptionFilter` when projecting host settings or
building requests. Publish request topics must be present and cannot contain MQTT wildcards.
Subscription filters may use `+` as a complete level and `#` only as the final
complete level. Both helpers reject null characters and oversized encoded topics.
`MqttPublishNode` validates publish request topics before calling `IMqttPublisher`;
`MqttTriggerNode` validates its static `TopicFilter` before opening a subscription.
