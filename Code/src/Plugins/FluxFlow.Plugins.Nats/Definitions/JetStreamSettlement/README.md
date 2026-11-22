# JetStream Settlement

Configuration type: `nats.jetstream.settlement`.

Sends an explicit settlement for a delivery from a **JetStream Consumer** in the
same workflow. Set `Consumer` to that component's name, not the broker's durable
consumer name. Place this component after the processing that determines success.

The Property Grid lists compatible consumers in the current workflow. A saved
reference that is missing or incompatible remains visible until you replace it;
opening the editor does not change the reference.

## Ports

`Input` accepts:

```json
{ "SettlementToken": "token-from-delivery", "Action": "Ack", "DelaySeconds": 0 }
```

- `Ack`: acknowledges processing and waits for the broker's acknowledgement.
- `Nak`: requests redelivery, optionally after `DelaySeconds` (0–86400).
- `Progress`: extends the acknowledgement wait without completing the delivery.
- `Terminate`: stops redelivery without reporting successful processing.

Command field names are case-insensitive. Omitting `Action` defaults to `Ack`.
Only `Nak` accepts a non-zero delay. `Output` forwards the original command after
successful execution; processing errors follow the shared error contract.
`Events` retains normal lifecycle diagnostics. `Capacity` bounds pending commands.

Tokens are scoped to a consumer instance and run. Repeated settlement, an unknown
token and tokens from previous runs are rejected. `Progress` keeps its token
available. Nak/Progress/Terminate confirm local transmission, not a separate
broker reply. A timeout or disconnect can leave a command's outcome ambiguous;
commands are not automatically retried. The consumer's operation timeout applies.

Stop permits settlement while accepted workflow work drains, then invalidates
remaining tokens. Unsettled messages remain governed by the broker's acknowledgement
wait and delivery limits. Nothing here provides an end-to-end transaction.
