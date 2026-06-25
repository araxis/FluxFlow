# FluxFlow.Components.Sessions

Standalone session recording, replay, and query nodes for FluxFlow. Each node is a
self-contained TPL Dataflow processor built on the `FluxFlow.Nodes` kit — `new` it,
`LinkTo` the next node, and run it without the engine.

## Nodes

| Node | Kit base | Shape |
|------|----------|-------|
| `SessionRecorderNode` | `FlowNode<SessionRecordInput, SessionRecord>` | `Input` -> `Output`, `Errors`, `Events` |
| `SessionReplayNode` | `FlowSource<SessionRecord>` | `Output`, `Errors`, `Events` (started via `StartAsync`) |
| `SessionQueryNode` | `FlowNode<SessionQueryRequest, SessionQueryResult>` | `Input` -> `Output`, `Sessions`, `Errors`, `Events` |

Every message travels as a `FlowMessage<T>` envelope. The recorder and query node carry
the incoming correlation id forward (`message.With(...)`); the replay source mints a
fresh correlation id per record. Domain failures surface on the broadcast `Errors` port
(`FlowError`, with the original correlation id where one exists) and the pump keeps
processing later messages; diagnostics go to `Events` (`FlowEvent`).

`BoundedCapacity` configures bounded input capacity for recorder/query nodes and
bounded source output capacity for replay. Replay awaits source output
acceptance while replay pacing remains deterministic. Output remains
broadcast/latest-wins; use a dedicated durable buffer if replay delivery must
guarantee no loss.

Session option records normalize optional text and copy tag maps at assignment.
Invalid capacity, replay range, replay mode, pacing, and query limit values are
rejected when assigned, so invalid configuration fails during node or factory
construction.

## Storage

Storage is injected by the host through `ISessionStore`, passed directly to each node's
constructor. This keeps database paths, schemas, workspace ownership, and retention
policy outside the component package. Hosts that need deterministic recording or replay
timing inject a `TimeProvider` (use `FakeTimeProvider` in tests).
Stores are expected to honor the non-null parts of `ISessionStore`; when a store
returns a null session, record, query result, or replay stream where the contract
requires a value, the node reports a clear session error instead of surfacing an
ambiguous null-reference failure.
Query results are also validated against the normalized query request. A store
that returns sessions outside the requested filters, or more sessions than the
requested limit, is reported through the query error port.

```csharp
ISessionStore store = new MySessionStore(...);
TimeProvider clock = TimeProvider.System;
```

## Recorder

```csharp
await using var recorder = new SessionRecorderNode(
    new SessionRecorderOptions { SessionId = "sample-session", BoundedCapacity = 128 },
    store,
    clock);

await recorder.Input.SendAsync(FlowMessage.Create(new SessionRecordInput { Name = "event", Payload = "..." }));
recorder.Complete();
await recorder.Completion;
```

`SessionRecorderNode` consumes `SessionRecordInput` and broadcasts the stored
`SessionRecord` returned by the store, carrying the correlation id forward. The session
is opened lazily on the first message. It is closed (with the final message count) when
the node is disposed; the close completes the `SessionCompleted` task. The injected
`TimeProvider` controls the session start/end timestamps and the default message
timestamp when `SessionRecordInput.Timestamp` is not set.

## Replay

```csharp
await using var replay = new SessionReplayNode(
    new SessionReplayOptions
    {
        SessionId = "sample-session",
        Mode = SessionReplayMode.Instant,
        BoundedCapacity = 128
    },
    store,
    clock);

await replay.StartAsync();
await replay.Completion;
```

Replay modes:

- `Instant`: emit records without delay.
- `FixedInterval`: wait `FixedIntervalMilliseconds` between records.
- `RealTime`: use timestamp deltas from the stored records.
- `Multiplier`: use timestamp deltas divided by `SpeedMultiplier`.

`StartSequence` and `MaxMessages` can limit the replay range. The injected
`TimeProvider` times the inter-record delays, so a `FakeTimeProvider` drives replay
deterministically. The loop stops when the session is exhausted, when the source is
completed/disposed, or when the output declines delivery. A missing session or store
failure surfaces a `FlowError` and faults the source.

## Query

```csharp
await using var query = new SessionQueryNode(
    new SessionQueryOptions { NamePrefix = "sample", Limit = 100, BoundedCapacity = 128 },
    store,
    clock);

await query.Input.SendAsync(FlowMessage.Create(new SessionQueryRequest { CorrelationId = "corr-1" }));
query.Complete();
await query.Completion;
```

`SessionQueryNode` consumes `SessionQueryRequest` and broadcasts a `SessionQueryResult`
on `Output`. When `EmitSessionOutputs` is enabled, each matching `SessionMetadata` is
also fanned out to the extra `Sessions` port (`FlowMessage<SessionMetadata>`). The
request can filter by name, name prefix, tags, started/ended ranges, active or completed
status, and limit; node options provide defaults that the request merges over. Invalid
requests and query failures surface on `Errors` and later requests continue.

## Contracts

The package owns the recording and replay contracts:

- `SessionRecordInput`
- `SessionRecord`
- `SessionMetadata`
- `SessionQueryRequest`
- `SessionQueryResult`
- `SessionStartRequest`, `SessionAppendRequest`, `SessionCompleteRequest`, `SessionReadRequest`
- `ISessionStore`

Records carry neutral fields: session id, sequence, timestamp, type, name, payload,
content type, and string attributes. Hosts can map their own envelope or event types
into these contracts.

Contract records normalize optional text by trimming it and treating blank values as
absent. Tag and attribute maps are copied with ordinal key comparison when assigned,
and nested session/input values are copied by the request/result contracts that carry
them.

## Composition

Building a workflow, reading config, creating nodes, and linking them is a
separate concern from the node package. This package is just the standalone
nodes and session contracts.

Use `FluxFlow.Components.Sessions.Composition` when a `FluxFlow.Composition`
host should register the optional session factories:

```csharp
services.AddKeyedSingleton<ISessionStore>("sessions", sessionStore);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterSessionRecorder()
        .RegisterSessionReplay()
        .RegisterSessionQuery());
```

The composition adapter binds the existing session option records from node
configuration, resolves the required store from the keyed `store` resource, and
can resolve an optional keyed `TimeProvider` resource named `clock`. Store
implementation, retention policy, and persistence setup remain host concerns.

The optional composition package also exposes
`SessionsComponentDesignMetadataProvider` for neutral Designer metadata over the
session composition node types. The standalone Sessions package remains free of
Designer, Composition, and Engine dependencies.
