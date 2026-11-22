# FluxFlow.Components.Mqtt

MQTTnet 5.2 supplies the native protocol client. Three components borrow one
DI-owned `MqttClientResource`, which owns the client and coordinates protocol
operations against connection changes. Core has no MQTT dependency.

| Component | Input | Output |
| --- | --- | --- |
| MqttConnection | MqttConnectionCommand: Connect / Disconnect | Command and connection state |
| MqttPublisher | MqttPublishRequest | Topic and native publish reason code |
| MqttSubscriber | MqttSubscriptionCommand: Subscribe / Unsubscribe | Received MQTT messages |

All have Input, Output and Events, with processing errors on Output. Connection and
Subscriber implement IFlowSource; start/stop them with the application. Options
accept the shared resource and output capacity (default 128). Each command input
uses a bounded, sequential TransformManyBlock. Outputs use native BroadcastBlock.

Command and result JSON retain declared property names, including `QoS`. JSON commands
are read case-insensitively. Payload content is not renamed or rewritten.

## Connection

The resource owns connection policy. Its MqttClientResourceOptions contains Client
(native MQTTnet options), AutoConnect (true) and ReconnectPolicy (native retry options).
The MQTT module references Microsoft.Extensions.Resilience, which supplies Polly's
retry types transitively; it has no direct Polly package reference.
One connection component forwards application start/stop to the resource, accepts
Connect/Disconnect commands, and exposes state events. Keep this component in the
workflow even when connection establishment is automatic; creating a resource alone
does not start network activity. Component options contain only Connection and OutputCapacity.
A Disconnect honors the resource's AutoConnect setting by default. Set
`"DisableReconnect": true` to stay disconnected until a new Connect command:

```json
{ "Command": "Disconnect", "DisableReconnect": true }
```

The flag defaults to false and applies only to Disconnect. A subsequent plain
Disconnect restores the configured AutoConnect behavior. Unexpected
connection loss starts a new resilience execution. The default policy retries with
a constant five-second delay and int.MaxValue retries. Hosts can supply
RetryStrategyOptions for finite counts, backoff, jitter, maximum delay and native
callbacks/predicates. Null permits one attempt without retries. Exhaustion emits
`mqtt.reconnect_exhausted` and waits for a new Connect command. Each attempt uses
the native client Timeout for connection and subscription restoration. Cancellation
interrupts both attempts and backoff; no connection semaphore is held during backoff.
Failures are ordinary error
outputs; `mqtt.connecting`, `mqtt.connected`, `mqtt.disconnecting` and
`mqtt.disconnected` notifications go to Events. Constructors never
connect. Shutdown stops reconnection and attempts a bounded graceful disconnect.
The resource is disposed by DI, after its consuming components.

Publisher does not wait for initial connection readiness or silently reconnect.
An offline publish produces a processing error. If a workflow must wait for connection,
use the connection command result or its connected event to initiate work.

## Publishing and payloads

```json
{ "Topic": "factory/temperature", "Payload": "21.5", "QoS": 1, "Retain": false }
```

Encoding defaults to Utf8. For arbitrary bytes use `"Encoding":"Base64"` and a
Base64 payload string. Serialize structured JSON explicitly in the upstream mapper
(for example JSONata `$string(...)`). QoS defaults to 0; values 0, 1 and 2 are supported.
Publish results retain the request's trace/correlation through WithValue. A successful
publish result represents the MQTT operation, not downstream business processing.

Received messages have this shape:

```json
{
  "Topic": "factory/temperature",
  "Payload": "MjEuNQ==",
  "Encoding": "Base64",
  "Text": "21.5",
  "QoS": 1,
  "Retain": false,
  "Timestamp": "2026-10-04T12:00:00+00:00"
}
```

Payload always preserves the original bytes. Text is null for invalid UTF-8.
Timestamp is local receipt time. Each incoming delivery creates a new envelope;
it is not a reply to the Subscribe command. Native receive handlers are attached
during construction, before connection, so retained/queued arrivals are observed.

## Subscriptions

One subscriber per resource supports multiple topic filters. Multiple publishers
may share that resource. Initial topics come from options/configuration; runtime
commands update the current set using exact filter strings:

```json
{ "Command": "Subscribe", "Topics": [{ "Topic": "factory/+/temperature", "QoS": 1 }] }
```

```json
{ "Command": "Unsubscribe", "Topics": [{ "Topic": "factory/+/temperature" }] }
```

Subscribe updates QoS for an existing filter. Unsubscribe removes that exact filter.
Commands while disconnected update the desired set without opening a connection.
Events report `mqtt.subscriptions_pending` or `mqtt.subscriptions_applied`; command
events preserve input lineage. Broker rejections produce processing errors and leave
the desired set available for correction or a subsequent reconnect.

Reconnect applies the current subscriptions, including removals made while offline
when the broker retained the session. Original configuration is not reapplied over
runtime changes. Resubscription may redeliver retained values; live changes only
subscribe the changed filters. Unsubscribe does not revoke already in-flight messages.
Subscription events are notifications, not a request/reply port.

Subscription changes are kept in memory for the subscriber's current run. Disposing a
subscriber removes its callbacks and, while connected, unsubscribes its owned filters.
Its replacement starts from the configured topics, even if the shared client stays
connected. Connection/subscriber claims are released on successful disposal. A replacement
connection component reapplies the resource's AutoConnect setting.

Stopping a connection component disconnects the shared client without disposing the
resource. Other users of that resource are affected by that disconnect. For independent
lifetimes, keep the connection controller in a long-lived workflow or use a workflow-local
resource. When reusing
a persistent broker session from an older instance, MQTT cannot enumerate its old
filters. Use a clean session when replacing the configured subscription set across
application restarts.

The component's broadcast output does not guarantee durable downstream delivery.
Automatic MQTT acknowledgement occurs after the receive callback accepts publication into the
output, not after downstream processing. Stopped or cancelled admission does not enable ACK.
Persistence remains a separate concern.

Subscriber also supports opt-in `AcknowledgementMode = Manual` for QoS 1 subscriptions,
with `MaxPendingAcknowledgements` (default 128) and an `ACK` terminal input accepting
`MqttAcknowledgementCommand`. Received QoS 1 values add `AcknowledgementId`; original payload
fields are preserved. Out-of-order feedback is held until preceding deliveries are acknowledged.
Capacity exhaustion faults rather than silently dropping work. QoS 0 deliveries have no ACK ID.
Stop and connection changes invalidate pending IDs; there is no implicit broker settlement drain,
automatic requeue or durable recovery. See the [subscriber guide](../Definitions/MqttSubscriber/README.md)
for the Request–Reply wiring and shutdown contract.

For configuration, use [the setup guide](Configuration/README.md).
The shared resource supports MQTT TCP, TLS, WebSocket and secure WebSocket addresses,
MQTT credentials, private CA roots and client certificates through that setup.
