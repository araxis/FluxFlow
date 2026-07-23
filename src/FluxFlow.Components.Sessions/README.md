# FluxFlow.Components.Sessions

Standalone session recording, replay, and query nodes over a host-owned
`ISessionStore`. Canonical record transport uses exact `FlowContent` and one
typed normal-result output; this package owns no store, serializer, composition
runtime, or engine.

## Canonical Nodes

| Node | Input | Output |
|------|-------|--------|
| `SessionRecorderNode` | `SessionContentRecordInput` | `FlowResult<SessionContentRecord>` |
| `SessionReplayNode` | source | `FlowResult<SessionContentRecord>` |
| `SessionQueryNode` | `SessionQueryRequest` | `FlowResult<SessionQueryOutcome>` |

Every canonical node exposes Events and Completion. Recorder and query have one
Input and one broadcast Output. Replay has one broadcast Output and is started
with `StartAsync`. There is no universal Errors data port. Validation, store,
missing-session, malformed-record, and query failures are ordinary results with
stable `SessionResultKinds` and `SessionErrorCodeNames`.

```csharp
ISessionStore store = ...; // opened and owned by the host
await using var recorder = new SessionRecorderNode(
    new SessionRecorderOptions
    {
        SessionId = "run-42",
        SessionName = "order intake"
    },
    store);

var results = new BufferBlock<FlowMessage<FlowResult<SessionContentRecord>>>();
recorder.Output.LinkTo(results);

var command = FlowMessage.Create(new SessionContentRecordInput
{
    Name = "received-order",
    Content = FlowContent.FromBytes(orderBytes, "application/json", "utf-8")
});

await recorder.Input.SendAsync(command);
recorder.Complete();
var result = await results.ReceiveAsync();
await recorder.Completion;
await recorder.SessionCompleted;
```

The result keeps the command correlation, trace, and headers, receives a new
message identity, and records the command message as its cause. Recorder opens
the session lazily after content validation and closes it after accepted input
and output have drained. A close failure faults `SessionCompleted` and emits a
diagnostic event without faulting normal node Completion.

`SessionContentRecordInput.Content` must have an original byte representation.
A value-only `FlowContent` returns `session.content_unavailable`; use an explicit
Serialization component first. The runtime writes a private versioned,
JSON-compatible envelope through the released `SessionRecord.Payload` store
boundary. Stores must preserve JSON object values. Hosts should not inspect or
depend on that private envelope.

## Replay And Query

`SessionReplayNode` preserves stored content bytes, content type,
encoding, metadata, and record order. Each replay output mints fresh source
identity. Missing sessions, source-read failures, and malformed stored records
are normal failure results; malformed records do not prevent later valid
records from replaying. `Instant`, `FixedInterval`, `RealTime`, and `Multiplier`
pacing remain available, with optional sequence and count limits.

`SessionQueryNode` returns one `SessionQueryOutcome`. `Count` always
describes the matches; `EmitSessionsInResult` controls whether copied
`SessionMetadata` entries are included. `SessionName` supplies the default exact
name filter without colliding with the component key-derived identity. Store
results are checked against the normalized filters and limit before emission.

## Ownership

The host owns `ISessionStore` and may use `ISessionStoreFactory`,
`SessionStoreContext`, and `SessionStoreLease` for explicit open/close scope.
Nodes borrow the store and never dispose it. A supplied `TimeProvider` controls
record defaults, result/event timestamps, session start/end, and replay pacing.

Keyed DI helpers remain available for direct hosts:

```csharp
services
    .AddFluxFlowSessionStore("sessions", store)
    .AddFluxFlowSessionStoreFactory("session-factory", sessionStoreFactory);
```

## Store Boundary

`SessionRecordInput` and `SessionRecord` remain the neutral object-valued
contracts implemented by session-store adapters. They are not alternate node
inputs or outputs. The nodes encode and decode a private exact-content envelope
at that boundary so adapter packages do not depend on `FlowContent` internals.

## Composition

The optional `FluxFlow.Components.Sessions.Composition` package owns workflow
factory registration, keyed store resolution, factory-lease disposal, and
Designer metadata. The runtime package remains free of Composition, Designer,
Hosting, concrete store, and Engine dependencies.
