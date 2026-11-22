# RabbitMQ Acknowledge

Component type: `amqp.settlement`. Settles a delivery explicitly after downstream
processing, without creating a feedback connection to its source.

## Ports

- **Input:** `SettlementToken` from a consumer delivery and `Action`: `Ack`, `Reject`
  or `Requeue`. Missing Action defaults to Ack.
- **Output:** the unchanged command after the native operation completes, or the
  existing workflow error contract on failure.
- **Events:** standard component lifecycle events.

Select **Consumer** from the RabbitMQ Consumers in the same workflow. Publishers,
other component types and consumers in other workflows are excluded. A missing
saved reference remains visible until explicitly replaced; opening the Inspector
does not change the configuration.
Place this block after the processing whose success permits acknowledgement.
Preserve the token in your intermediate message/attributes and map it into:

```json
{ "SettlementToken": "token-from-delivery", "Action": "Ack" }
```

An acknowledgement is a one-way AMQP command, not a broker-confirmed transaction.
The block cannot prove arbitrary external side effects completed. It never settles
automatically on local buffering and never retries ambiguous acknowledgements.
Tokens from another consumer, a closed channel or previous run are rejected.
Unsettled messages can be redelivered after disconnect or Stop.
