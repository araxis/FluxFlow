# Sessions Component Package

Date: 2026-06-01

## Decision

Add `FluxFlow.Components.Sessions` as a separate component package with two
initial nodes:

- `session.recorder`
- `session.replay`

The durable concept is the session. Recording and replay are operations over
that session, so the package uses `Sessions` rather than a narrower package
name.

## Boundaries

The package owns:

- neutral session contracts
- recorder input handling
- ordered append requests
- replay timing modes
- cancellation and completion behavior
- structured errors and diagnostics

The host owns:

- concrete persistence
- database paths and schema
- UI/session browser behavior
- envelope-specific mapping and display fields

Storage is abstracted through `ISessionStoreFactory` and `ISessionStore`.

## Contracts

The first slice includes:

- `SessionRecordInput`
- `SessionRecord`
- `SessionMetadata`
- `SessionStartRequest`
- `SessionAppendRequest`
- `SessionCompleteRequest`
- `SessionReadRequest`
- `ISessionStore`
- `ISessionStoreFactory`

`SessionRecord` carries session id, sequence, timestamp, type, name, payload,
content type, and string attributes.

## Replay Modes

Replay supports:

- `instant`
- `fixedInterval`
- `realTime`
- `multiplier`

`startSequence` and `maxMessages` limit the replay range.

## Deferred

`session.source` is deferred. For now `session.replay` is the source node. If a
second name proves useful later, it can be added as a thin alias or helper.

Concrete storage adapters are also deferred. A separate adapter package can be
added later if one store implementation becomes broadly reusable.

## Verification

Focused coverage includes:

- recorder writes messages in order
- recorder reports append failures and continues
- replay emits messages in order
- fixed interval replay timing
- multiplier replay timing
- cancellation during replay
- replay diagnostics
- missing session startup failure
- option validation
- store injection requirement
- node registration

Planned release tag:

```text
components-sessions-v0.1.0-alpha.1
```
