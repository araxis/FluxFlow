# FluxFlow.Components.Sessions

Reusable session recording and replay components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `session.recorder` | `Input` -> `Output`, `Errors` | Records incoming messages into a host-provided session store. |
| `session.replay` | `Output`, `Errors` | Replays stored session messages as a source stream. |
| `session.query` | `Input` -> `Output`, `Sessions`, `Errors` | Queries session metadata from a host-provided session store. |

## Storage

The package owns the recording and replay contracts, but storage is injected by
the host through `ISessionStoreFactory`. This keeps database paths, schemas,
workspace ownership, and retention policy outside the component package.

```csharp
registry.RegisterSessionsComponents(options =>
    options.UseStore(context => new MySessionStore(context.StoreName)));
```

Hosts that need deterministic recording or replay timing can provide a clock:

```csharp
registry.RegisterSessionsComponents(options => options
    .UseStore(context => new MySessionStore(context.StoreName))
    .UseClock(sessionClock));
```

## Recorder

```json
{
  "type": "session.recorder",
  "name": "recorder",
  "store": "default",
  "sessionId": "sample-session",
  "boundedCapacity": 128,
  "tags": {
    "source": "demo"
  }
}
```

`session.recorder` consumes `SessionRecordInput` and emits the stored
`SessionRecord` returned by the store. Startup opens the session. Completion
closes the session with the final message count.

`UseClock(...)` controls session start/end timestamps and default message
timestamps when `SessionRecordInput.Timestamp` is not set.

## Replay

```json
{
  "type": "session.replay",
  "name": "replay",
  "store": "default",
  "sessionId": "sample-session",
  "mode": "instant",
  "boundedCapacity": 128
}
```

Replay modes:

- `instant`: emit records without delay.
- `fixedInterval`: wait `fixedIntervalMilliseconds` between records.
- `realTime`: use timestamp deltas from the stored records.
- `multiplier`: use timestamp deltas divided by `speedMultiplier`.

`startSequence` and `maxMessages` can limit the replay range.

`UseClock(...)` controls replay delays for fixed interval, real-time, and
multiplier modes. Without it, sessions use the system clock.

## Query

```json
{
  "type": "session.query",
  "name": "query",
  "store": "default",
  "namePrefix": "sample",
  "limit": 100,
  "boundedCapacity": 128
}
```

`session.query` consumes `SessionQueryRequest` and emits `SessionQueryResult`
on `Output`. When `emitSessionOutputs` is enabled, each matching
`SessionMetadata` is also emitted on `Sessions`.

The request can filter by name, name prefix, tags, started/ended ranges, active
or completed status, and limit. Query failures are emitted through `Errors` and
later requests continue.

## Contracts

The package includes:

- `SessionRecordInput`
- `SessionRecord`
- `SessionMetadata`
- `SessionQueryRequest`
- `SessionQueryResult`
- `ISessionStore`
- `ISessionStoreFactory`
- `ISessionClock`

Records carry neutral fields: session id, sequence, timestamp, type, name,
payload, content type, and string attributes. Hosts can map their own envelope
or event types into these contracts.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
