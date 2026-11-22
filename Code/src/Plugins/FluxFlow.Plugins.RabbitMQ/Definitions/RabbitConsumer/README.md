# RabbitMQ Consumer

Receives AMQP 0.9.1 deliveries from a queue using a named `amqp.connection`.
Consumption always requires explicit settlement. Enqueuing a delivery into a
workflow buffer does not acknowledge it.

Component type: `amqp.consumer`.

## Ports

- **Output:** `SettlementToken`, `Exchange`, `RoutingKey`, Base64 `Body`, original
  basic `Properties`, `Redelivered` and `ConsumerTag`.
- **Input:** a settlement command for a delivery produced by this consumer.
- **ACK:** accept a delivery using its `SettlementToken`.
- **NACK:** reject a delivery using its `SettlementToken`. **Requeue on NACK**
  chooses whether to return it to the queue; the default is off (dead-letter or
  discard according to broker policy).
- **Events:** standard component lifecycle events.

Route downstream feedback to ACK/NACK. Either accepts the original delivery or
an object containing `SettlementToken`. The port determines the outcome; an
`Action` property does not override it.
Keep the token when mapping the payload; TraceId and business CorrelationId are
not delivery identifiers. A Request–Reply result must be mapped to the original
delivery's token and routed according to the outcome you want to settle.

```json
{"SettlementToken":"copy-the-token-from-the-delivery"}
```

ACK/NACK use bounded terminal/control inputs, so feedback can return from
downstream without a business-data cycle. They do not emit a second delivery on
Output. Malformed, duplicate, wrong-consumer and stale feedback produces an
`amqp.settlement_rejected` diagnostic event without settling another delivery.

Existing Input settlement commands and RabbitMQ Acknowledge blocks remain
supported unchanged:

```json
{
  "SettlementToken": "copy-the-token-from-the-delivery",
  "Action": "Ack"
}
```

Actions are `Ack`, `Reject` (discard/dead-letter according to broker policy) and
`Requeue`. Acknowledge only after your required processing succeeds. The ordinary
Input is available for independent command paths; downstream feedback should use
ACK/NACK instead. The optional **RabbitMQ Acknowledge** block references this
consumer and uses the same channel and pending deliveries. This is not a distributed
transaction or an exactly-once processing guarantee.

## Configuration

Select the connection and edit the queue, unacknowledged delivery limit, buffer
capacity and maximum message size directly in the Property Grid. The shared
**Broker declarations** groups provide inline Add/Edit/Remove for exchanges,
queues and bindings. Each declaration includes an **Arguments** collection with
typed visual Add/Edit/Remove, including nested arrays and tables. Bytes use Base64;
Int64 values retain exact decimal text. Saving an argument validates it through the
existing configuration API and creates one Undo/Redo step. Cancel discards the draft.
**Advanced JSON** remains optional for the complete configuration.

`Connection` names an `amqp.connection`; `Queue` names a queue. `Prefetch` limits
unacknowledged deliveries and must be positive. `Capacity` bounds local buffers;
`MaxMessageBytes` rejects oversized deliveries explicitly.

Optional `Topology.Exchanges`, `Topology.Queues` and `Topology.Bindings` declare
entities before subscribing. Leave them empty when using existing entities with
restricted permissions. Queue declaration example:

```json
{
  "Type": "amqp.consumer",
  "Connection": "broker",
  "Queue": "readings",
  "Prefetch": 32,
  "Topology": { "Queues": [{ "Name": "readings", "Durable": true }] }
}
```

Credentials are host environment-variable references on the connection; their
values do not belong in project files. MQTT connections are not compatible.

## Recovery and Stop

The official client can recover a lost connection and subscription. Broker
channel errors and consumer cancellation are reported as failures. Settlement
tokens are invalid after channel loss, Stop or rerun. Unsettled messages are left
for broker redelivery when the channel closes. A settlement is an AMQP one-way
operation, not a broker-confirmed transaction.

Stop cancels the subscription, closes the owned channel and rejects late
settlement commands. ACK/NACK stop accepting new work; pending deliveries are
not automatically acknowledged during Stop. Requeue on NACK only affects explicit
NACK feedback while running. Existing queues, exchanges and brokers are never deleted.
Output bytes and AMQP metadata are copied before native callback memory expires.
