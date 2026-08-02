# vNext FlowValue State

Date: 2026-07-19

## Status

The twenty-first bounded vNext milestone is implemented on local branch
`work/state-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone keeps State as a domain component while moving its dynamic data
and expected outcomes to canonical workflow contracts.

## Canonical Runtime

- `FlowValueStateReducerNode` consumes `FlowValueStateReducerInput` and emits
  one `FlowResult<FlowValueStateReducerResult>` Output plus Events.
- Commands carry an exact immutable `FlowValue` input, optional per-command
  initial state, immutable ordinal variables, and Reduce, Reset, or Clear.
- Results preserve key, previous state, input, new state, operation, version,
  update time, and complete `FlowMessage<T>` lineage.
- Updated, reset, and cleared operations are successful result variants.
  Invalid messages and keys, key-expression and reducer failures, and key-limit
  rejection are normal error variants. Later accepted commands continue.
- Stable string error codes are public through `StateErrorCodeNames`; immutable
  error details retain the released numeric `StateErrorCodes` value for
  migration and diagnostics.
- Reducer and optional key expressions compile once. Context data remains
  `FlowValue`; there is no serialization round trip or implicit CLR mapping.
- Ordered single-worker execution preserves deterministic keyed state changes.
  Clocks remain injectable and diagnostics remain separate from domain state.

## Composition And Designer

- `RegisterStateReducer()` now owns the canonical fixed command/result
  contract. The descriptor exposes Events and no universal Errors port.
- A private auditable binding record lets the factory accept ordinary JSON
  `initialState` values and decode them to `FlowValue`; definitions do not use
  the tagged canonical serialization form.
- Required expression-engine and optional clock resources remain host-owned and
  resolve from exact addresses. The `engine` option remains diagnostic metadata
  rather than DI selection.
- Designer metadata now reports `FlowValueStateReducerInput` and
  `FlowResult<FlowValueStateReducerResult>` fixed ports while retaining existing
  option/resource hints.
- Package examples use the flat `Resources` and `Workflows` application shape.

## Compatibility And Versioning

- `FluxFlow.Components.State` moves from `3.0.5` to `4.0.0`.
- `FluxFlow.Components.State.Composition` moves from `1.4.0` to `2.0.0` because
  its parameterless fixed registration changes to canonical ports.
- `StateReducerNode`, object-based input/result/options, direct result Output,
  Errors, Events, and runtime behavior remain available for code-authored
  compatibility. No implicit conversion exists between legacy and canonical
  links.
- The source-declaration baseline records the additive runtime declarations and
  the Composition binding/factory change.
- SDK package validation passes for State `4.0.0` against published `3.0.5` and
  State Composition `2.0.0` against published `1.4.0`.
- Release preflight and complete package dry-runs pass for both packages from
  the seeded temporary current-package source outside tracked repository state.

## Verification

- State runtime tests: 28 passed.
- State Composition tests: 15 passed.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 2,057 tests across 63 projects with no
  failures or warnings.
- Controlled Debug and Release builds completed across 130 projects with zero
  warnings and zero errors. Cold traversals exceeded their command bounds;
  build-server shutdown and controlled incremental reruns completed cleanly.
- A package-only net8 consumer restored only State Composition `2.0.0`, used
  the transitive State/Data/Composition contracts, asserted canonical port
  metadata and result/error declarations, and printed
  `STATE_PACKAGE_CONSUMER_OK`.

## Deferred Boundaries

- No implicit mapper, universal error port, Engine dependency, state
  persistence backend, eviction policy, public polling API, or legacy contract
  rewrite was introduced.
- State storage remains node-local. Durable/shared state belongs to separately
  planned Storage or resource work.
- Legacy Composition `1.x` remains the definition compatibility line; this
  milestone does not rewrite existing documents automatically.

## Next Gate

Assess Projections as the next bounded component-family pass. Preserve its
domain snapshot/event semantics separately from diagnostics while adopting
canonical result conventions where they improve linkable outcomes.
