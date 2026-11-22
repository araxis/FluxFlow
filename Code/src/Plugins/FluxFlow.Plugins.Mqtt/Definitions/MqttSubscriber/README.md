# MQTT Subscriber

Receives messages from MQTT topics. Configuration type: `mqtt.subscriber`.

## Setup

Set **Connection** to a named `mqtt.client` resource and configure **Topics**, for example:

```json
[
  {
    "Topic": "factory/+/temperature",
    "QoS": 1
  }
]
```

Keep one MQTT Connection Controller and at most one subscriber per resource.
One subscriber supports multiple topic filters. The controller establishes the connection;
this component manages subscriptions and receives messages.

In the Property Grid, use **Topics → Add** to enter a topic filter and QoS. Hover over a topic row
or focus its controls to edit or remove it. Save applies one undoable change; Cancel leaves it unchanged.
New topics default to QoS 1 in Manual mode and QoS 0 in Automatic mode. Switching modes does not change existing topics.

## Ports

- **Output** emits received values with `Topic`, Base64 `Payload`, `Encoding`, `Text`,
  `QoS`, `Retain` and receipt `Timestamp`. `Text` is null for invalid UTF-8.
- **Input** accepts Subscribe/Unsubscribe commands to change topics during execution.
- **ACK** settles a broker delivery using `{ "AcknowledgementId": "..." }` in Manual mode.
  Settlement does not declare that business processing succeeded.
  In Automatic mode, the canvas hides unused ACK ports and dims wired ACK ports. Existing wiring is retained when switching modes.
- **Events** reports subscription changes; these are not correlated replies to received data.
- **Error** filters processing errors from Output.

```json
{
  "Command": "Unsubscribe",
  "Topics": [
    {
      "Topic": "factory/+/temperature"
    }
  ]
}
```

Reconnect restores the current desired subscriptions. Restarting the subscriber starts
again from configured Topics. Disposal removes its callbacks and owned subscriptions.
`OutputCapacity` defaults to 128.

## Acknowledgement

`AcknowledgementMode` defaults to `Automatic`, preserving existing projects and the received-message shape.
Automatic acknowledges admission to Output, not downstream processing. A stopped subscriber does not
acknowledge a delivery it did not forward. Output remains an in-memory broadcast, not a durable queue.

In the Inspector, **Acknowledgement** offers **Automatic** and **Manual** for broker acknowledgement.
Manual is saved as `"AcknowledgementMode": "Manual"`; existing configuration remains compatible.
**Pending limit** is shown only in Manual mode. Switching to Automatic
hides the field without deleting its saved value. QoS 0 has no broker acknowledgement; QoS 2 uses automatic
protocol handling.

Choose **Manual** with QoS 1 topic subscriptions to defer broker acknowledgement. Each received QoS 1
value includes an opaque `AcknowledgementId` alongside its unchanged payload fields. Send that ID to
ACK when the workflow decides the delivery is handled. Feedback may arrive concurrently or out of order;
PUBACK packets are sent in receive order. Thus an unsettled earlier delivery holds up later ACKs.

`MaxPendingAcknowledgements` defaults to 128 and includes deliveries whose ACK is waiting for an earlier
one. Exceeding the limit faults the subscriber visibly without acknowledging excess work. No unbounded
pending list or automatic discard is used. ACK commands are also buffered with a finite capacity.
QoS 0 publications can still arrive through a QoS 1 subscription; they have no acknowledgement ID and
cannot be acknowledged. Manual QoS 2 subscriptions are not supported in this version.

Unknown, duplicate, disconnected and previous-run IDs produce `mqtt.acknowledgement_rejected` Events.
Successful native ACK sends produce `mqtt.acknowledged`; failures fault the subscriber through the
existing diagnostics. A sent PUBACK is not a confirmation that the broker persisted anything.

Stop or disconnect invalidates all outstanding IDs and reports their count through
`mqtt.acknowledgements_abandoned`. Stop closes ACK; it does not drain broker settlements or manufacture
successful outcomes. Request–Reply's own graceful draining is unchanged, but results arriving after
the MQTT subscriber stops cannot settle its old deliveries. Reconnect deliveries get fresh IDs.
Redelivery requires suitable broker session settings and broker retention; a clean session does not
promise recovery. Withholding an ACK is not an immediate requeue, and MQTT 5 failure ACKs are not
RabbitMQ-style NACK/requeue commands. This component exposes no broker NACK input.

## Connect to Request–Reply

```text
Subscriber.Output → Request–Reply.Input
Request–Reply.Request → processing → Request–Reply.ACK / NACK
Request–Reply.Output → [Outcome = 'ACK'] → mapper → Subscriber.ACK
```

The mapper uses the existing JSONata editor:

```jsonata
{ "AcknowledgementId": Request.AcknowledgementId }
```

Only route outcomes whose `Request.AcknowledgementId` exists. Handle NACK, Timeout and NotDispatched
on separate workflow branches; they do not implicitly acknowledge or requeue a broker message.
Keep the ID from the original Request, not the processing Reply payload.

Filtering only ACK outcomes is an example policy. A NACK outcome can also be acknowledged after the
workflow records or otherwise handles the failure: route it through the same mapper to Subscriber.ACK.
This settles the broker delivery without changing the Request–Reply outcome to success.

For example, these component definitions can be placed alongside a `mqtt.connection` using `broker`:

```json
{
  "receive": {
    "Type": "mqtt.subscriber", "Connection": "broker",
    "Topics": [{ "Topic": "work", "QoS": 1 }],
    "AcknowledgementMode": "Manual", "MaxPendingAcknowledgements": 128,
    "Output": "exchange.Input", "ACK": "settle.Output"
  },
  "exchange": {
    "Type": "requestReply", "MaxInFlight": 1,
    "Request": "worker.Input", "ACK": "worker.Output",
    "Output": { "Address": "settle.Input", "Engine": "jsonata",
      "Condition": "Outcome = 'ACK' and $exists(Request.AcknowledgementId)" }
  },
  "settle": {
    "Type": "mapper", "Engine": "jsonata",
    "Expression": "{'AcknowledgementId': Request.AcknowledgementId}"
  }
}
```

Replace `worker` with the actual processing path. Route its business failure path to `exchange.NACK`
as appropriate. Processing concurrency is still owned by Request–Reply; the subscriber only manages
broker acknowledgement. A process crash after business work but before PUBACK can cause redelivery,
so this is not an exactly-once business-processing guarantee.
