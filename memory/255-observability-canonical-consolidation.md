# Observability Canonical Consolidation

Date: 2026-07-23

## Status

The Observability consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, package publication, pull request,
or merge was performed.

## Canonical Runtime

- `FlowCounterNode`, `FlowLoggerNode`, and `FlowMetricsNode` now exclusively
  consume `FlowMessage<FlowValue>` and emit one ordered
  `FlowMessage<FlowResult<T>>` Output plus Events.
- Counter rejection remains a successful result carrying current count state.
  Predicate evaluation failure remains normal error data and later inputs
  continue.
- Logger input and selected attributes remain immutable FlowValue data.
  Multiple selector failures collapse into one partial result carrying the
  usable `FlowLogEntry`; substituted template values are not recursively
  expanded.
- Metrics preserves deterministic timestamps and rates, updates count/rate on
  selector failure, and averages size over sized observations only.
- Expression engines, FlowValue context factories, selectors, and clocks remain
  constructor-injected host resources. All nodes preserve ordering, fan-out,
  diagnostics, completion draining, and message lineage.

## Removed Compatibility

- Removed generic `FlowCounterNode<TInput>`, `FlowLoggerNode<TInput>`, and
  `FlowMetricsNode<TInput>` implementations, their direct outputs, Errors
  streams, object selector support, and duplicate node helper pipeline.
- Moved the canonical implementations from temporary `FlowValue*Node` names to
  the concise node names and similarly consolidated options, `FlowLogEntry`,
  and `IObservabilityValueSelector`.
- Removed `ObservabilityErrorCodes`, compatibility-only `InputType`, `Engine`,
  and `SizeSelector` option members, and duplicate temporary contracts.
- String error names, result kinds, snapshots, diagnostics, context contracts,
  expression fallback, and one-or-many selector configuration remain.

## Composition And Metadata

- `RegisterCounter()`, `RegisterLogger()`, and `RegisterMetrics()` are the only
  registrations and expose fixed FlowValue/FlowResult ports with Events.
- Removed the three generic registration overloads and obsolete generic
  factories. Composition tests now activate components through the canonical
  application revision host.
- Designer metadata uses concise options, `FlowResult<FlowLogEntry>`, and the
  non-generic FlowValue selector. Compatibility-only `omittedOptions`
  attributes were removed; aliases and exact host-owned resource hints remain.

## Versioning And API Review

- `FluxFlow.Components.Observability`: `4.0.0` to `5.0.0`.
- `FluxFlow.Components.Observability.Composition`: `2.2.0` to `3.0.0`.
- Published comparison baselines are runtime `3.0.2` and Composition `1.4.0`.
- The source-declaration baseline changes package index 27 from 179 to 111
  declarations and index 28 from 26 to 23 declarations.
- SDK package validation reports the expected runtime CP0001 removals and
  CP0002 log-entry/option member removals for both target frameworks. Composition
  reports only CP0002 for the three removed generic registration overloads.
  No suppressions were added.

## Verification

- Observability runtime: 13 passed, zero warnings.
- Observability Composition: 23 passed, zero warnings.
- Composition: 145 passed.
- Composition Hosting: 46 passed; its cold build reported the 11 existing
  obsolete legacy-runtime warnings.
- Designer: 112 passed, zero warnings.
- Release: 96 passed, zero warnings.
- Controlled Debug build: 131 projects, zero warnings and zero errors.
- Controlled Release build: succeeded; the cold traversal reported 38 known
  legacy Composition warnings and the immediate controlled rerun completed all
  131 projects with zero warnings and zero errors.
- Formatting verification passed for changed runtime/composition/test paths.
- Both release preflights passed.
- A fresh temporary source contained all 58 current packages; runtime `5.0.0`
  and Composition `3.0.0` archive checks, smoke consumers, feed checks, and
  release dry-runs passed against it.
- A package-only net8 consumer compiled with warnings as errors, constructed
  all three concise nodes, implemented the selector contract, validated the
  fixed logger metadata, exercised the canonical log-entry shape, and printed
  `OBSERVABILITY_CANONICAL_API_OK`.

## Remaining Cleanup

The Observability ledger entry is `removed-after-parity`. Remaining program
work starts with canonical configuration and Composition migrations, followed
by Engine legacy-runtime removal, structural Control/Routing cleanup, and MQTT
legacy contract/adaptor consolidation. Each remains a separate bounded pass.
