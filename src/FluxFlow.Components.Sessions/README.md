# FluxFlow.Components.Sessions

Reusable session recording and replay components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `session.recorder` | `Input` -> `Output`, `Errors` | Records incoming messages into a host-provided session store. |
| `session.replay` | `Output`, `Errors` | Replays stored session messages as a source stream. |

## Storage

The package owns the recording and replay contracts, but storage is injected by
the host through `ISessionStoreFactory`. This keeps database paths, schemas,
workspace ownership, and retention policy outside the component package.

```csharp
registry.RegisterSessionsComponents(options =>
    options.UseStore(context => new MySessionStore(context.StoreName)));
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

## Contracts

The first slice includes:

- `SessionRecordInput`
- `SessionRecord`
- `SessionMetadata`
- `ISessionStore`
- `ISessionStoreFactory`

Records carry neutral fields: session id, sequence, timestamp, type, name,
payload, content type, and string attributes. Hosts can map their own envelope
or event types into these contracts.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
