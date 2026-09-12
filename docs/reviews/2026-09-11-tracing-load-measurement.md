# Current-runtime tracing load measurement

Full recording has a substantial measured cost, especially in deep graphs.
Performance acceptance remains open. This slice changes the benchmark harness and
its tests, not production library code or package APIs.

## What was measured

The existing native-backed load harness now offers off/unsampled/propagation/recorded
modes and an optional fixed input parent. It retains counters, not activities. Every
run verifies exact sampling/start/stop/full-data/parent-correlation counts against
actual delivered messages plus warmup times the number of processing hops. It also
preserves exact envelope identity, target counts, bounded tracking, latency histograms
and drain checks. Native custom sinks are not counted as processing wrapper scopes.

Thirty-two count smokes cover both frameworks, direct/update hosting, one-hop/one-target
and 32-hop/eight-target graphs, and all four tracing modes. Thirty-two timed direct
runs compare off versus recorded: four alternating fresh-process pairs per framework
and shape. Each uses the same current binaries, shared 64-KiB JSON payload, fixed input
parent, capacity 16, four producers, 64 warmup messages and ten-second production.
No build or other workload test was launched during measurement.

This measures processing instrumentation plus bounded counter callbacks and the load
harness. It does not include SDK/exporter/backend, logging or metric collection.
Pass-through envelopes retain their original carrier; new producer spans and per-hop
derived-carrier allocations are outside this workload. This is end-to-end load
evidence, separate from microbenchmark iteration timing.

## Results

Rates are medians of runs; percentage and allocation changes are medians of paired
differences. They need not equal ratios/differences of the separate median columns.

| Framework | Hops / targets | Off messages/s | Recorded messages/s | Paired rate change | Extra bytes/completion |
|---|---:|---:|---:|---:|---:|
| net8 | 1 / 1 | 573,162 | 368,000 | -35.17% | 1,226 |
| net8 | 32 / 8 | 169,157 | 12,929 | -92.40% | 60,582 |
| net10 | 1 / 1 | 559,943 | 396,453 | -29.74% | 1,216 |
| net10 | 32 / 8 | 169,527 | 34,713 | -79.57% | 40,973 |

All sixteen pairs show lower throughput and higher allocation with recording. Deep
graph median p99 bucket upper bounds increase from 15.11 to 75.04 ms on net8 and
15.02 to 41.36 ms on net10. These are per-message histogram upper bounds, not exact
percentiles or a latency SLO. Raw CPU, latency, queue samples and retained-heap deltas
remain in each report. Heap results vary by shape/mode and do not establish leak freedom.

Do not attribute the entire difference to Core without profiling. The observer itself
updates shared atomic counters from concurrent stages, which can contribute contention.
Next separate observer counter cost from activity/context/tag cost, especially the
large net8 deep-graph increase, and preserve equivalent payload/delivery semantics.
Do not remove tracing or reliable delivery to make the number look better.

## Evidence and qualification

- Final full nonincremental Release build: zero warnings/errors.
- Selected local regression: 4,792 passes in 81 reports, no failed/skipped tests.
- Discovery: 5,002 including all 21 new cases; 1,485 stable source inputs.
- Focused load qualification: 67 tests, including direct/update fast/slow tracing.
- Measurements: all 32 smoke and 32 timed runs pass; 1,485 source inputs and 122
  runtime files remain unchanged. Both runs report 20 logical processors and workstation
  GC; actual runtimes are .NET 8.0.30 and .NET 10.0.11.
- No public API/baseline or production implementation change in this slice.

Authoritative artifacts: artifacts/core-refactoring/tracing-load/verification.json,
measurements/verification.json, measurements/comparison.json, all per-run reports/logs
and source/binary inventories. summarize.ps1 computes the descriptive paired summaries.
The testing skill drove independent report/count assertions and actual parent validation.

| Requirement | Exact test |
|---|---|
| Explicit mode/context and defaults | Tracing_mode_and_input_context_are_explicit_load_options |
| Unknown tracing-mode rejection | Load_options_reject_unknown_or_invalid_values |
| Actual observed processing and bounded delivery/drain | Traced_runtime_load_has_exact_bounded_observation_and_delivery |

The test project targets net10; net8 support is demonstrated by actual binary smoke
and timed runs, not claimed as additional unit tests. The assertion/pseudo-mutation
review is in .testagent/tracing-load-review.md; it is not a coverage or mutation score.

## Remaining gates

The selected regression excludes acceptance, container, external smoke and database
integration suites. No complete performance or release sign-off follows. Exporter/log/
metric workloads, optional-host timed observation, conditions/payload matrix, actual
shutdown/swaps under load, longer retention, remaining operation boundaries and a fresh
coherent local package candidate remain required. No SLO was invented, nothing was
published and no external consumer work was coordinated. The full goal remains active.
