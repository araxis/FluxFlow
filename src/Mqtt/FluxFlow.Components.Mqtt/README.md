# FluxFlow.Components.Mqtt

Transport-neutral MQTT client orchestration and standalone workflow components.
The package uses `FluxFlow.Coordination` for workflow acknowledgement and
`FluxFlow.Resilience` for retry schedules while retaining all MQTT-specific
classification and lifecycle behavior. It does not depend on Engine,
Composition, hosting, or a concrete MQTT client library.

MQTT is one component family in FluxFlow. It does not define a separate
application model, runtime, dependency-injection container, or error channel.

## Ownership

- `MqttBrokerConfiguration` describes an endpoint and transport defaults. It
  does not own a connection.
- `MqttClientConfiguration` describes one logical client identity and owns its
  resolved credentials, certificates, Last Will, desired subscriptions,
  auto-connect mode, and reconnect policy.
- One `MqttClientController` owns one neutral transport session for one logical
  client.
- Multiple logical clients may share the same broker configuration without
  sharing identity, credentials, connection state, subscriptions, or reconnect
  state.
- Multiple workflow components may share one controller.
- `IMqttTransportFactory` and `IMqttTransportSession` form the concrete-adapter
  boundary.
- The host owns controller lifetime. Components never dispose a shared
  controller.

Credentials and certificates supplied to the core configuration are already
resolved host values. Secret lookup and inline-secret policy remain host
responsibilities.

## Portable broker transports

`Transport` selects the wire transport and `UseTls` selects whether that
transport is secured. Existing definitions omit `Transport` and continue to
use TCP.

| `Transport` | `UseTls` | Connection |
| --- | --- | --- |
| `Tcp` | `false` | TCP |
| `Tcp` | `true` | TLS over TCP |
| `WebSocket` | `false` | `ws` |
| `WebSocket` | `true` | `wss` |

For WebSocket connections, `WebSocketPath` defaults to `/mqtt`:

```csharp
var broker = new MqttBrokerConfiguration
{
    Host = "broker.example.net",
    Port = 443,
    Transport = MqttBrokerTransport.WebSocket,
    UseTls = true,
    WebSocketPath = "/mqtt"
};
```

`ServerName` is a TCP/TLS target-host override. WebSocket connections use
`Host` for WSS certificate and server-name validation, so an independent
`ServerName` is rejected. Browser hosts should expose WebSocket/WSS and reject
native-only settings such as client-certificate loading in their own
capability validation; the neutral MQTT core does not detect host platforms.

## Components

| Class | Canonical type | Ports | Purpose |
|---|---|---|---|
| `MqttControlNode` | `mqtt.command` | `Input`, `Output`, `Events` | Executes Connect, Disconnect, Status, Publish, Subscribe, and Unsubscribe requests. |
| `MqttPublishOperationNode` | `mqtt.publish` | `Input`, `Output`, `Events` | Publishes exact content through a referenced client. |
| `MqttSubscriptionTriggerNode` | `mqtt.receive` | `Output`, `Ack`, `Nak`, `Events` | Emits received messages and accepts payload-independent workflow outcomes. |
| `MqttClientEventsNode` | `mqtt.events` | `Output`, `Events` | Emits connection, subscription, and reconnect domain events. |

Expected operation failures are `MqttClientFailureResult` values on normal
`Output`. Results expose `Kind`, `Operation`, `IsError`, `Error`, and
`Timestamp`, so links and mappers can route failures without CLR type checks.
There is no universal Error or State port. Unexpected implementation and
lifecycle faults remain observable through component completion.

`MqttControlOptions` supports semantic request behavior:

- `RequestProcessing`: `Sequential` or `Concurrent`.
- `ResultOrder`: `PreserveInput` or `Completion`.
- `MaximumConcurrentRequests` limits concurrent execution.
- `MaximumPendingRequests` bounds accepted pending work.

Lifecycle and subscription mutation remain serialized inside the controller.
Publish and status operations may run concurrently. An explicit Disconnect
suppresses reconnect until Connect or host restart.

## Standalone C#

```csharp
var controller = new MqttClientController(
    new MqttClientConfiguration
    {
        Name = "Resources.Messaging.Client1",
        ClientId = "orders-client",
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

await using var publish = new MqttPublishOperationNode(controller);
await publish.Input.SendAsync(FlowMessage.Create(new MqttPublishMessage
{
    Topic = "orders/accepted",
    Content = FlowContent.FromBytes(payload, "application/json"),
    Qos = MqttQos.AtLeastOnce
}));
```

The controller is a facade. Internal collaborators separately own connection
and reconnect lifecycle, command dispatch, validation, result construction,
desired subscriptions and trigger claims, received-message dispatch, broker
outcome aggregation, and client-event publication.

Reconnect delay, attempt, duration, and jitter calculations come from the
transport-neutral resilience package. MQTT still decides which failures are
retryable, suppresses reconnect after an explicit disconnect, restores desired
subscriptions, resets lifecycle state, performs provider operations, and emits
MQTT domain events. Production jitter uses a varying random source; hosts and
tests may inject an `IRetryJitterSource` when deterministic samples are needed.

## Content

`MqttPublishMessage` and `MqttReceivedApplicationMessage` use immutable
`FlowContent`. Exact payload bytes and content metadata cross the adapter
boundary once and may be shared safely during workflow fan-out.

The receive component does not guess whether payload bytes contain JSON, text,
XML, or another format. Use serialization, validation, payload inspection, or
mapping components when a workflow needs another representation.

## Commands And Results

`MqttClientRequest` uses the JSON discriminator `Operation`. Supported request
types are:

- `MqttConnectRequest`
- `MqttDisconnectRequest`
- `MqttStatusRequest`
- `MqttPublishClientRequest`
- `MqttSubscribeRequest`
- `MqttUnsubscribeRequest`

`MqttClientResult` uses the JSON discriminator `Kind`. Successful variants are
operation-specific. Invalid requests, disconnected operations, authorization
failures, and other expected transport failures use
`MqttClientFailureResult`. Cancellation requested by the caller remains
cancellation rather than result data.

## Subscriptions

Named subscriptions belong to the client. Inline subscriptions belong to one
receive component. A trigger accepts one or more `MqttSubscriptionTarget`
values:

```csharp
new MqttSubscriptionTriggerOptions
{
    TriggerId = "Orders.Receive",
    Subscriptions =
    [
        MqttSubscriptionTarget.Named("SharedAlerts"),
        MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
        {
            TopicFilter = "orders/+",
            Qos = MqttQos.AtLeastOnce
        })
    ]
};
```

Desired named and inline subscriptions are restored after reconnect. Missing
named subscriptions wait for later creation. Trigger claims are exclusive by
identity and by identical resolved topic filter. Different overlapping filters
remain valid. A publication matching several subscriptions in one trigger is
emitted once with all matching subscription labels.

## Acknowledgement

Workflow acknowledgement and broker acknowledgement are separate:

- `MqttWorkflowAcknowledgement.None` or `Required` controls workflow Ack/Nak
  signals.
- Workflow signals ignore payload type and match pending delivery `TraceId`.
- Pending workflow outcomes use the shared bounded coordinator; no timeout task
  or cancellation source is allocated per delivery.
- `MqttBrokerAcknowledgement.Automatic` completes independently of workflow
  outcome.
- `AfterHandoff` completes after output acceptance.
- `AfterOutcome` maps Ack, Nak, or timeout to the adapter.
- QoS 0 never performs broker acknowledgement.

Deferred policies are rejected when the selected adapter does not advertise
the required capability. The first matching workflow outcome wins. Duplicate,
late, conflicting, and unknown outcomes are diagnostic events.

Broker outcome aggregation remains entirely MQTT-owned. The shared coordinator
knows only the delivery `TraceId`, context, deadline, and workflow outcome; it
never invokes a provider-specific acknowledgement API directly.

## Composition

The optional `FluxFlow.Components.Mqtt.Composition` package registers the
canonical component types and binds broker, client, subscription, and retry
resources. The core runtime remains usable without Composition or Engine.

### Canonical JSON

The optional Composition package binds the core from a flat application:

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
      "Commands": {
        "Type": "mqtt.subscription",
        "TopicFilter": "commands/+",
        "Qos": "AtLeastOnce"
      },
      "Client1": {
        "Type": "mqtt.client",
        "Broker": "Resources.Messaging.Broker1",
        "ClientId": "orders-client",
        "Subscriptions": "Resources.Messaging.Commands",
        "AutoConnect": "OnStart"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Receive": {
        "Type": "mqtt.receive",
        "Client": "Resources.Messaging.Client1",
        "Subscription": "Commands",
        "WorkflowAcknowledgement": "Required",
        "BrokerAcknowledgement": "AfterOutcome",
        "Output": "Handle.Input"
      },
      "Handle": {
        "Type": "orders.handle"
      }
    }
  }
}
```

A trigger `Subscription` may be one string, one inline object, or a mixed array
of those forms. `Qos` is the canonical option name. Components remain directly
inside workflows; there are no Nodes, Links, Composition, or per-component
resource wrapper sections.

## Migration From 4.x

Version 6 removes the parallel publisher/trigger API:

- replace `IMqttPublisher` and `MqttPublishNode` with
  `IMqttClientController` and `MqttPublishOperationNode`
- replace `IMqttTriggerSource`, `IMqttSubscription`, and `MqttTriggerNode` with
  controller trigger registration and `MqttSubscriptionTriggerNode`
- replace byte-array request and received-message contracts with
  `MqttPublishMessage`, `MqttReceivedApplicationMessage`, and `FlowContent`
- replace `MqttTriggerResponse` with Ack/Nak signals matched by `TraceId`
- replace client-health interfaces with `MqttClientEventsNode`
- replace Errors links with conditions over normal `MqttClientResult` values
- move concrete connection ownership to a transport adapter

No compatibility shim recreates the removed 4.x runtime path.
