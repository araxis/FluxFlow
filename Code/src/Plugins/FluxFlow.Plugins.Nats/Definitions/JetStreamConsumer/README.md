# JetStream Consumer

Configuration type: `nats.jetstream.consumer`.

Pulls messages from a durable JetStream consumer. It shares a run-owned
`nats.connection` with publishers and other consumers.

## Ports and delivery

`Output` emits the original bytes and repeated headers, broker metadata and an
opaque settlement token:

```json
{
  "SettlementToken": "opaque-delivery-token",
  "Message": { "Subject": "events.reading", "DataBase64": "AP8B", "Headers": {} },
  "Metadata": {
    "Stream": "READINGS", "Consumer": "worker",
    "Sequence": { "Stream": 1, "Consumer": 1 },
    "NumDelivered": 1, "NumPending": 0,
    "Timestamp": "2026-01-01T00:00:00Z", "Domain": ""
  }
}
```

`Input` accepts explicit settlement commands. Prefer **JetStream Settlement**
after downstream processing, referencing this consumer, to avoid a feedback link.
`Events` carries shared lifecycle diagnostics. Local buffering never acknowledges
delivery. Broker redelivery is possible; downstream operations must handle duplicates.

## Existing or provisioned entities

Select `Stream` and durable `Consumer`. Existing consumers must use pull delivery
and explicit acknowledgements. Their server-side filters, acknowledgement wait,
delivery limits and retention are retained, not overwritten by project defaults.
Existing use requires consumer info, pull, acknowledgement and inbox permissions;
it does not require creation, update or deletion privileges.

**Create stream if missing** and **Create consumer if missing** are separate,
opt-in choices. New consumers support All/New initial delivery, an optional subject
filter, finite `AckWaitSeconds` and `MaxDeliver`. `MaxInFlight` also supplies the
new consumer's broker `MaxAckPending`. Provisioned streams have finite retention.
Neither existing nor newly provisioned broker entities are deleted on Stop.

## Bounds, Stop and recovery

`MaxInFlight` bounds locally retained unsettled deliveries, including redeliveries.
Pulling pauses when that bound is reached. After an ACK is confirmed or a Terminate
command is sent, all local tokens for that stream sequence expire and their slots
become available. `Capacity` bounds workflow buffering;
`MaxMessageBytes` rejects oversized messages without acknowledging them.
The broker controls redelivery and delivery limits. No automatic acknowledgement
or progress extension occurs. Acknowledgement waits must cover downstream work.

Stop cancels the outstanding pull and stops new deliveries. Terminal settlement
remains available while accepted downstream work drains. Completion invalidates
tokens; tokens from another consumer or run are rejected. Unsettled messages
remain subject to broker redelivery. Exhausted reconnects fail production.

Managed-container JetStream storage is removed with the container on Stop.
Choose an existing NATS service when durable state must survive Studio reruns.
