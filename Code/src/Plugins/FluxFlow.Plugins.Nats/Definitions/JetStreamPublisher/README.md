# JetStream Publisher

Configuration type: `nats.jetstream.publisher`.

Stores a message in a named JetStream stream and waits for its broker publish
acknowledgement. This is distinct from Core NATS Publisher.

## Ports and message

`Input` accepts the same byte-preserving message shape as Core NATS:

```json
{
  "Subject": "events.reading",
  "DataBase64": "AP8B",
  "Headers": { "Nats-Msg-Id": ["reading-123"], "Correlation-Id": ["sample"] }
}
```

Do not supply `ReplyTo`: JetStream owns the acknowledgement inbox. `Output`
contains `Message` (the original input fields), `Stream`, `Sequence` and
`Duplicate`. `Duplicate: true` means the broker recognised `Nats-Msg-Id` within
its configured duplicate window; a new message was not stored. This is not an
end-to-end exactly-once processing guarantee. `Events` uses the shared diagnostic
contract. Processing errors remain errors, not successful acknowledgements.

## Setup

Reference a `nats.connection`, and select `Stream`. By default the stream must
already exist; publishing requires no stream administration calls. Enable
**Create stream if missing** explicitly to provision it. A new stream uses the
configured subject, File/Memory storage and finite message, byte and age limits.
Existing settings are never updated. Conflicting concurrent creation fails
instead of replacing broker configuration. No stream is deleted on Stop.

Configure the connection's authentication and TLS through execution-host
environment-variable references. Never store credentials in a project.

## Delivery and lifecycle

`TimeoutSeconds` bounds startup and each publication. `Capacity` bounds buffered
workflow messages; `MaxMessageBytes` rejects oversized payloads without truncation.
There is one publish attempt. A timeout or disconnect may leave the result
unknown; the component never automatically retries an ambiguous publication.
Native reconnect uses the shared connection's finite policy. Graceful Stop drains
accepted commands within their deadlines; cancellation aborts outstanding work.

For a managed NATS service, enable JetStream in its startup file:

```text
port: 4222
jetstream { store_dir: /tmp/jetstream }
```

Managed-container storage is temporary and removed on Stop. Use an existing NATS
service for persistence across Studio runs. Environment profiles, registry/image
settings and locked infrastructure placements reuse the NATS service editor.
