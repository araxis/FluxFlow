# State Canonical Consolidation

Date: 2026-07-23

## Status

The State family consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, publication, pull request, or
merge was performed.

This milestone supersedes the additive compatibility state recorded in
`224-vnext-state-flowvalue.md`. State now has one maintained component runtime
and Composition contract. Sessions remains a separate cleanup-ledger pass.

## Canonical Contract

- `FlowValueStateReducerNode` accepts typed `FlowValueStateReducerInput`
  commands and emits one `FlowResult<FlowValueStateReducerResult>` Output.
- Reduce, reset, and clear remain successful result kinds. Invalid messages,
  keys, key expressions, reducers, and key limits remain normal error results;
  later commands continue.
- Per-key state is immutable `FlowValue` data. Processing is serial and ordered,
  request-level initial state overrides the option for a new key, and clear
  resets the next version to one.
- Reducer and key expressions compile once through the required host-owned
  `IFlowExpressionEngine`; the optional host-owned clock controls result and
  event timestamps.
- Results and Events preserve key/version data, expression metadata, engine
  name, bounded key-limit diagnostics, fan-out, correlation, trace, causation,
  and input ordering.

## Removed Compatibility Surface

- Removed `StateReducerNode`, `StateReducerInput`, `StateReducerResult`, and
  `StateReducerOptions`.
- Removed numeric `StateErrorCodes`; normal failures now expose only stable
  string codes from `StateErrorCodeNames`.
- Removed the object node's Errors stream plus its internal `IFlowReducer` and
  `CompiledFlowReducer` implementation path.
- Removed unused `FlowValueStateReducerOptions.Engine` and the duplicate
  Composition `engine` option metadata. The required keyed `engine` resource
  remains the only engine-selection contract.
- Migrated State Composition tests from the obsolete composition runtime to
  canonical flat application definitions, revision hosting, and stable ports.
- Updated Designer resource picker patterns to exact `Resources.{name}`
  addresses.

CLR consumers now convert state and input values explicitly at the application
boundary and route outcomes through conditions over `FlowResult.Kind`,
`IsError`, and `Error.Code`.

## Versioning And Compatibility

- `FluxFlow.Components.State` moves from `4.0.0` to `5.0.0`.
- `FluxFlow.Components.State.Composition` moves from `2.2.0` to `3.0.0`.
- Source-declaration baseline entries changed only for manifest indexes 50 and
  51: State from 95 to 57 declarations and State Composition from 21 to 20.
- SDK package validation against State `4.0.0` reports only the five removed
  public types and dead engine-option accessors on both target frameworks.
- State Composition remains binary compatible with `2.2.0`. No compatibility
  suppression was generated.

## Verification

- State runtime tests: 18 passed with no warnings.
- State Composition tests: 15 passed through canonical hosting with no actual
  compiler warnings.
- Core Composition tests: 145 passed.
- Composition Hosting tests: 46 passed with 11 existing migration warnings.
- Designer tests: 112 passed.
- Release tests: 96 passed.
- Controlled Debug build: succeeded with 27 warnings and no errors.
- Controlled Release build: succeeded with 65 warnings and no errors.
- A fresh temporary source outside the repository was seeded with all 58
  current manifest packages.
- Release preflight and local-source dry-run passed for both changed packages,
  including archive inspection, isolated smoke restore/build, and feed checks.
- A combined package-only `net8.0` consumer restored State `5.0.0` and State
  Composition `3.0.0`, built with warnings as errors, loaded flat
  `Resources`/`Workflows` JSON, resolved the keyed expression engine, and
  printed `STATE_CANONICAL_API_OK` after verifying state, result, lineage, and
  absence of a universal Errors port.
- `graphify update . --force` refreshed the ignored local graph to 16,546
  nodes, 35,640 edges, and 950 communities; HTML generation was skipped at the
  configured size limit.

## Next Gate

Audit Expectations as the remaining half of the earlier Assertions and
Expectations family grouping, then audit Sessions independently. Preserve
projection-event semantics and session store/replay behavior before removing
their compatibility contracts.
