# Observability Component Package

Date: 2026-06-01

## Decision

Add `FluxFlow.Components.Observability` as a separate package-owned component
family for generic observer nodes. The package should emit neutral records that
hosts can route into logs, dashboards, files, tests, or external collectors
without leaking application workspace concepts into reusable contracts.

## Package Shape

Implemented nodes:

| Node type | Ports | Purpose |
|-----------|-------|---------|
| `flow.logger` | `Input` -> `Entries` | Emits structured `FlowLogEntry` records. |
| `flow.metrics` | `Input` -> `Snapshots` | Emits `FlowMetricSnapshot` records with count, rates, timestamps, and optional size values. |
| `flow.counter` | `Input` -> `Snapshots` | Emits `FlowCounterSnapshot` records with optional predicate filtering. |

Shared package contracts:

- `FlowLogEntry`
- `FlowLogLevel`
- `FlowMetricSnapshot`
- `FlowCounterSnapshot`
- `IObservabilityContextFactory`
- `IObservabilityValueSelector<TInput>`
- `ObservabilityNodeContext`

## Behavior

- Input shape is selected through host-registered type aliases.
- Built-in selectors `input` and `value` return the original input.
- Hosts can register custom selectors for logger attributes and metric sizes.
- Counter predicates use a host-registered expression engine only when a
  predicate is configured.
- Per-message selector and predicate failures emit `FlowError` and allow later
  messages to continue.
- Bounded capacity is configurable and validated.
- Nodes emit package diagnostics for observed, rejected, emitted, and failed
  operations.

## Boundaries

The package does not include:

- dashboard models
- storage
- application log scope enums
- protocol-specific fields
- test runner concepts
- external collector adapters

Those stay in the consuming application or future adapter packages.

## Verification

Focused tests cover:

- module registration
- counter counting and predicate rejection
- counter predicate errors
- logger entry formatting and attribute selectors
- logger selector failures
- metrics count, rate, and size selection
- metrics selector failures
- clean completion behavior

Release tag:

```text
components-observability-v0.1.0-alpha.1
```
