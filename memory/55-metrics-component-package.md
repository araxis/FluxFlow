# Metrics Component Package

Date: 2026-06-01

## Decision

Add `FluxFlow.Components.Metrics` as a separate component package with one
initial node:

- `metrics.aggregate`

The package is application-neutral. Hosts adapt their own envelopes, domain
events, request data, or transport messages into `MetricSampleInput`.

## Contracts

The first slice includes:

- `MetricSampleInput`
- `MetricSnapshotOutput`
- `MetricGroupSnapshot`

`MetricSampleInput` supports timestamp, name, group, value, unit, size, and
tags. Missing numeric values count as samples but do not affect numeric totals
unless `treatMissingValueAsZero` is enabled.

## Behavior

`metrics.aggregate` consumes metric samples and emits snapshots. It preserves
input order through a serial input block. Rates are calculated from sample
timestamps, which keeps tests deterministic and avoids hidden clocks for normal
stream aggregation.

The output is bounded and non-blocking. If the snapshot output is full,
processing continues. Dropped snapshots emit a structured `FlowError` and
diagnostic. Unlinked outputs do not block input processing.

Group cardinality is bounded by `maxGroups`. Samples for rejected new groups
still update global totals, but the rejected group is not added. The node emits
one structured error per rejected group key.

## Options

The node supports:

- `rateWindowSeconds`
- `boundedCapacity`
- `maxGroups`
- `emitEverySample`
- `trackLatest`
- `trackMinMax`
- `trackSize`
- `groupByTag`
- `treatMissingValueAsZero`

## Verification

Focused coverage includes:

- count, value, and size aggregation
- deterministic current and average rates from sample timestamps
- grouping by tag
- final-only snapshot emission
- missing values without numeric zero
- optional missing-value zero behavior
- maximum group limit behavior
- invalid sample continuation
- unlinked/full output completion
- diagnostics
- registration and option validation

Planned release tag:

```text
components-metrics-v0.1.0-alpha.1
```
