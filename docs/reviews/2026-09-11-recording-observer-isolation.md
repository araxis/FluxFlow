# Full-recording observer isolation

## Result

Removing the additional start/stop observer counters did not consistently improve
the deep graph across runtimes. It does not support attributing the previously
measured large full-recording cost to those callbacks alone. Performance remains
unaccepted; the next step is process-local CPU/allocation profiling of the recording
path, not removing tracing or changing delivery semantics.

| Actual runtime | Counting observer messages/s, median | Sampling-only messages/s, median | Paired median rate change | Paired median allocation change, bytes/completed | Median p99 upper bound, counting → sampling-only |
|---|---:|---:|---:|---:|---:|
| 8.0.30 | 12,666 | 11,898 | -6.05% | +2,776 | 75.04 → 76.28 ms |
| 10.0.11 | 35,593 | 36,903 | +4.99% | -425 | 39.62 → 39.40 ms |

The net8 rate differences range from -7.53% to +5.36% (one positive pair, three
negative). All four net10 differences are positive, +1.06% to +5.98%. Paired median
CPU time per completion changes +6.37% / -7.94%. Heap-delta medians are mixed and
are not evidence of leak freedom. These are descriptive short-run observations,
not statistical significance claims. The changes do not establish exact counter
cost, nor prove that all remaining cost belongs to Core.

## Method and validation

An artifact-local, dependency-free startup hook installs a sampling-only listener
for the existing Core activity source. Both variants request full recording. The
hook retains one atomic sampling counter but has no start/stop callbacks or span
storage. Its sidecar checks exactly `(completed + 64) * 32` sampling requests.
The counting variant uses the unchanged harness listener and additionally verifies
scope starts/stops, full-data and input-parent correlation, and zero active scopes.

The reduced variant runs the harness with its internal listener `off`. This raw
configuration is preserved honestly: it does not mean effective recording is off,
and its zero harness span counters are not observed span-lifetime counts. Run
manifests explicitly record effective sampling, hook path and sidecar. The hook is
enabled only for each synthetic child process; environment settings are cleared
afterward. No persistent installation or unrelated-process collection occurred.

All runs use the same previously qualified benchmark and runtime files: direct
Core graph, 32 hops, eight targets, capacity 16, four producers, shared immutable
64-KiB JSON payload, fixed input parent and 64 warmup messages. Four 256-message
count checks precede four alternating ten-second pairs per runtime. All 20
subprocesses exit successfully, with exact reliable target delivery, admission
accounting, bounded tracking and drained business queues/handlers.

Verification retains 1,485 unchanged qualification inputs, 122 unchanged runtime
files, two unchanged probe source/project inputs and the unchanged probe assembly
hash. Payload SHA-256 remains
`521AF24A0CDFB480F069EBCC28AF7AD1C7D144D8C34088C151DA185A19C3C38B`.
The probe builds with zero warnings/errors. Its initial sandbox build could not
read the SDK's configuration; the approved empty-feed build succeeded with no new
external packages. No concurrent build/test or affinity/priority adjustment was
used during measurements. Both processes report 20 logical processors and
workstation GC.

No production source, public API, baseline or permanent harness changed. The
previous 4,792-pass, 81-report regression / 5,002-case discovery checkpoint remains
the qualification for those identical inputs; it was not rerun or relabeled as a
fresh regression. No unit tests were added in this diagnostic-only slice.

## Limits and next action

The sampling callback still uses a shared atomic counter. The external hook and
absence of the counting observer's active-scope idle check also differ; business
queue/handler checks and graph stop remain. Allocation includes the harness and
scheduling effects. The reduced observer does not independently validate span
lifetimes. This experiment therefore cannot measure a zero-observer path or
isolate exact contention, context, tag, activity or scheduler costs.

Next capture synthetic deep-workflow CPU and sampled-allocation evidence using
the current verified binaries. Record tool/runtime identity, launch arguments,
timestamps and hashes; check trace event presence/loss before attribution. Traced
runs are diagnostic evidence, not timing comparisons. This turn collected no new
CPU/allocation trace. SDK/exporter/log/metric collection, optional-host timing,
broader workloads, long retention and fresh local packages remain open.
No publishing or external consumer coordination.

Evidence: `artifacts/core-refactoring/counter-isolation/measurements/verification.json`,
`comparison.json`, ordered `runs.json`, raw reports, probe sidecars and logs.
The scoped README and scripts describe and reproduce the protocol without
overwriting existing evidence.
