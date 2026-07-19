# vNext FlowValue Observability

Date: 2026-07-19

## Status

The twenty-fourth bounded vNext milestone is implemented on local branch
`work/observability-vnext`. No push, tag, publication, pull request, or merge
was performed.

This milestone adds canonical FlowValue Counter, Logger, and Metrics components
while preserving every released generic node as an explicit compatibility
surface.

## Canonical Runtime

- `FlowValueCounterNode` consumes FlowValue and emits one
  `FlowResult<FlowCounterSnapshot>` Output plus Events. Counted and
  predicate-rejected inputs are successful `counter-snapshot` and
  `counter-rejected` variants; expected predicate failures are normal
  `counter-failed` results.
- `FlowValueLoggerNode` emits one `FlowResult<FlowValueLogEntry>` Output. Log
  input and attributes stay immutable FlowValue data. Multiple selector failures
  collapse into one `log-entry-partial` error result carrying the usable entry.
- `FlowValueMetricsNode` emits one `FlowResult<FlowMetricSnapshot>` Output.
  Size-selection failure, including negative or non-finite numeric size, emits
  one `metric-snapshot-partial` result carrying the updated count/rate snapshot.
- `IObservabilityFlowValueSelector` keeps workflow data in the canonical value
  model without object conversion or serialization round trips.
- All three nodes process in acceptance order, preserve clocks, counts, rates,
  templates, message lineage, fan-out, clean completion, and later-input
  continuation. None exposes a universal Errors port.

## Composition And Designer

- Parameterless `RegisterCounter()`, `RegisterLogger()`, and `RegisterMetrics()`
  own the canonical fixed FlowValue/FlowResult contracts.
- Explicit generic registration overloads retain the complete released direct
  Output and Errors contracts.
- Canonical expression contexts and selectors resolve by exact host-owned
  resource address. The Metrics `sizeSelector` resource is the sole canonical
  selector setting, avoiding an option/resource name collision in flat JSON.
- Canonical Logger accepts one `attributeSelectors` entry as a string and
  multiple entries as an array, then resolves exact `attribute:{name}` resources.
- Designer metadata reports canonical fixed ports and omits only documented
  compatibility-only `inputType`, engine diagnostic, and selector diagnostic
  options.

## Compatibility And Versioning

- `FluxFlow.Components.Observability` moves from `3.0.2` to `4.0.0`.
- `FluxFlow.Components.Observability.Composition` moves from `1.4.0` to `2.0.0`
  because parameterless fixed ports and error behavior change.
- `FlowCounterNode<TInput>`, `FlowLoggerNode<TInput>`, and
  `FlowMetricsNode<TInput>` retain released options, object selectors, direct
  Outputs, Errors ports, Events, and runtime behavior.
- The source-declaration baseline records additive runtime declarations and
  canonical registration overloads; no released declaration was removed or
  signature-changed.
- SDK package validation passes for Observability `4.0.0` against published
  `3.0.2` and Observability Composition `2.0.0` against published `1.4.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source outside tracked state.

## Verification

- Observability runtime tests: 36 passed.
- Observability Composition tests: 26 passed.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 2,088 tests across 63 projects with no
  failures or warnings.
- Final controlled Debug and Release builds completed across 130 projects with
  zero warnings and zero errors. Cold traversals exposed the same pre-existing
  transient nullable test warning seen in the prior milestone; warmed controlled
  reruns were clean.
- A package-only net8 consumer restored Observability Composition `2.0.0`,
  asserted all three canonical registration types and public constants, used the
  FlowValue selector contract, constructed all canonical nodes, and printed
  `OBSERVABILITY_PACKAGE_CONSUMER_OK`.

## Deferred Boundaries

- Canonical log entries remain workflow data; this package does not own logging,
  tracing, metric exporters, or sink lifetimes.
- No implicit mapper, universal error port, renderer, polling API, Engine
  dependency, or host-specific service framework was introduced.
- Legacy Composition `1.x` remains the stored-definition compatibility line.

## Next Gate

Assess Sources as the next bounded component-family pass. Preserve natural
source lifecycle semantics while selecting canonical FlowValue/FlowContent
output contracts and normal result behavior without inventing a fake input.
