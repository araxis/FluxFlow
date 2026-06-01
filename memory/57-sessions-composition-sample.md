# Sessions Composition Sample

Date: 2026-06-01

## Goal

Add a runnable sample that demonstrates `FluxFlow.Components.Sessions` while
keeping concrete storage in the host.

## Decisions

- Sample project: `samples/FluxFlow.SessionsCompositionSample`.
- Package-owned nodes:
  - `session.recorder`
  - `session.replay`
- Host-owned sample nodes:
  - `sample.session-input`
  - `sample.session-sink`
- Host-owned store:
  - `InMemorySessionStore`

The sample intentionally does not introduce a reusable storage package. It
shows the current intended boundary: the package defines storage contracts and
runtime behavior; the host provides persistence.

## Flow

Recording run:

```text
sample.session-input -> session.recorder -> sample.session-sink
```

Replay run:

```text
session.replay -> sample.session-sink
```

The two runs share the same in-memory store instance.

## Lifecycle Note

The recording source starts in a later phase than `session.recorder`. This
ensures the recorder opens its session before the finite source emits records.

## Status

Implemented as a deterministic command-line sample with package registration,
host-owned storage, diagnostic collection, and README/docs entries.
