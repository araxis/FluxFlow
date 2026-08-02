# vNext Metric Results

Date: 2026-07-19

## Status

The twenty-third bounded vNext milestone is implemented on local branch
`work/metrics-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone preserves Metrics as a typed domain component while making
snapshots, partial applications, and expected failures normal linkable results.

## Canonical Runtime

- `FlowMetricsAggregateNode` consumes typed `MetricSampleInput` messages and
  emits one `FlowResult<MetricSnapshotOutput>` Output plus Events.
- Per-sample snapshots use `snapshot`; coalesced completion uses
  `final-snapshot`; invalid samples use `aggregate-failed`; partial group-limit
  applications use `group-limit-reached`.
- Invalid samples emit stable `metrics.invalid_sample` or
  `metrics.aggregate_failed` errors without mutating aggregate state, and later
  accepted samples continue.
- Group-limited samples still update the global aggregate but skip per-group
  itemization. Each such input emits exactly one error-shaped result carrying
  the updated snapshot as Value. Distinct rejected-group tracking is bounded.
- Ordered single-worker processing preserves count, value, size, latest,
  min/max, grouping, event-time rate, and output-order behavior.
- Normal completion drains accepted samples and, when per-sample emission is
  disabled, emits exactly one final snapshot with complete lineage from the
  last accepted sample.

## Composition And Designer

- `RegisterMetricsAggregate()` now owns the canonical fixed result contract.
  The descriptor keeps typed `MetricSampleInput` Input, one result Output,
  Events, and no universal Errors surface.
- Coalesced final snapshots are part of normal composition completion; no
  host-specific flush hook is required.
- The optional clock remains host-owned and resolves from an exact resource
  address.
- Designer metadata reports `FlowResult<MetricSnapshotOutput>` Output while
  retaining existing option and clock-picker hints.
- Package examples use the flat `Resources` and `Workflows` application shape.

## Compatibility And Versioning

- `FluxFlow.Components.Metrics` moves from `3.0.4` to `4.0.0`.
- `FluxFlow.Components.Metrics.Composition` moves from `1.4.0` to `2.0.0`
  because its fixed Output and error contract change.
- `MetricsAggregateNode`, its direct snapshot Output, Errors port, Events, and
  released aggregation behavior remain unchanged for code-authored
  compatibility.
- The source-declaration baseline records the additive runtime declarations;
  no released declaration was removed or signature-changed.
- SDK package validation passes for Metrics `4.0.0` against published `3.0.4`
  and Metrics Composition `2.0.0` against published `1.4.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source outside tracked state.

## Verification

- Metrics runtime tests: 47 passed.
- Metrics Composition tests: 14 passed.
- Composition tests: 126 passed.
- Designer tests: 98 passed.
- Composition Hosting tests: 38 passed.
- Release convention tests: 93 passed.
- The complete Release sweep passed 2,076 tests across 63 projects with no
  failures or warnings.
- Final controlled Debug and Release builds completed across 130 projects with
  zero warnings and zero errors.
- A package-only net8 consumer restored Metrics Composition `2.0.0`, used its
  transitive runtime/Data/Composition contracts, asserted canonical port types
  and result/error names, constructed the canonical node, and printed
  `METRICS_PACKAGE_CONSUMER_OK`.

## Deferred Boundaries

- No FlowValue conversion was added because metric samples and snapshots are
  stable domain contracts used directly by telemetry producers and consumers.
- No implicit mapper, universal error port, metric exporter, renderer, polling
  API, or Engine dependency was introduced.
- Legacy Composition `1.x` remains the definition compatibility line; this
  milestone does not rewrite existing documents automatically.

## Next Gate

Assess Observability as the next bounded component-family pass. Preserve its
generic typed runtime compatibility while selecting explicit canonical data and
normal-result contracts for counter, logger, and metric observation behavior.
