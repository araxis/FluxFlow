# Mapping And Control Sample

Date: 2026-06-01

## Goal

Add a small runnable sample that demonstrates package composition without an
external transport dependency.

## Decisions

- Sample project: `samples/FluxFlow.MappingControlSample`.
- Host-owned nodes:
  - `sample.orders`
  - `sample.order-sink`
  - `sample.assertion-sink`
- Package-owned nodes:
  - `flow.mapper`
  - `flow.filter`
  - `flow.when`
  - `flow.assert`
- The sample uses a small host-provided expression engine with expression keys.
- The host registers type aliases and context factories for its own message
  types.
- The sample stays deterministic and does not require an external service.

## Flow

```text
source -> flow.mapper -> flow.filter -> flow.when -> priority / standard sinks
                         |
                         `-> flow.assert -> assertion sink
```

## Status

Implemented as a command-line sample with package registration, typed aliases,
context factories, and diagnostic collection.
