# FluxFlow.Components.Sessions

Standalone session recording, replay, and query nodes over a host-owned session
store.

- `SessionRecorderNode`: `SessionContentRecordInput` -> `SessionContentRecord`.
- `SessionReplayNode`: source of `SessionContentRecord` with pacing and
  cancellation.
- `SessionQueryNode`: `SessionQueryRequest` -> `SessionQueryOutcome`.

Record content uses exact `FlowContent`; adapter-facing store records remain
neutral and private envelopes preserve persistence metadata. Query bounds,
sequence continuation, replay pacing, completion, deterministic clocks, and
fan-out remain intact.

Expected query outcomes are typed. Store or operation failure becomes
`FlowError` on normal Output. The host owns the store and optional clock.

## Composition

Install `FluxFlow.Components.Sessions.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
