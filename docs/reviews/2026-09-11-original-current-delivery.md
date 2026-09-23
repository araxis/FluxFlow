# Original-to-current standalone delivery

The current candidate has a confirmed standalone delivery regression in this
matched workload: **16–32% higher mean operation time and 21–80% more allocation**.
Both opposite-order pairs show the regression at every tested topology. This
closes the representative original-to-current measurement gap; it is not a general
performance acceptance or a claim about production throughput or p99 latency.

## Results

One operation sends a reused immutable message through serial forwarding nodes,
awaits every terminal target, and returns the last result. Setup checks exact
message reference identity at the targets. The body is the saved original
`SendThroughPipelineAndFanout` benchmark, unchanged apart from line endings and
terminal newline. It uses public native output links, not named application routes.

| Hops / targets | Original mean µs | Current mean µs | Time increase | Original bytes/op | Current bytes/op | Allocation increase | Time increase in the two pairs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 / 1 | 3.589 | 4.160 | 15.9% | 1,176 | 1,421.5 | 20.9% | 14.9%, 16.9% |
| 8 / 2 | 22.813 | 27.903 | 22.3% | 4,778 | 7,701 | 61.2% | 26.2%, 18.5% |
| 32 / 8 | 70.082 | 92.427 | 31.9% | 16,984.5 | 30,499.5 | 79.6% | 33.5%, 30.3% |

Means and bytes are averages of two process results per implementation/topology.
Each process has the same 15 actual iterations. Pair percentages compare adjacent
opposite-order runs; no process-population confidence interval is inferred from
only two processes. Per-process means, iteration-level confidence margins,
allocations, full report paths and hashes are in the
[machine-readable summary](../../artifacts/core-refactoring/final-delivery-comparison-20260911/summary.json).

## Matched settings and provenance

- Original: isolated reconstruction of commit
  `4d11bd889679aa40970be18f8d4e0772732f2137`. The captured initial dirty inventory
  and status contain no selected node-source changes/deletions. All 30 extracted
  source/build files match original Git content after only CRLF-to-LF normalization;
  28 were materialized with CRLF. Archive and materialized source hashes are also
  recorded. These are rebuilt sources, **not preserved original executable bytes**.
- Current: exact `0.0.0-local.20260911.startup.1` Core package, with generated
  process DLL hash checked against the candidate. All 1,488 qualified source inputs
  stayed unchanged. No production/test/existing benchmark source or package changed.
- Release/net10.0, runtime 10.0.11, SDK 10.0.400, i7-12700H, Windows
  10.0.26200.9278, x64, no debugger. Existing High performance plan checked before
  and after, never changed. Runs were sequential without concurrent build/test load.
- Same existing benchmark dependency version, 0.15.8. Original central package
  configuration was initially dirty, but the relevant benchmark and Dataflow
  declarations match. Both executed dependency manifests use inbox Dataflow on
  net10.0. The inherited package-icon build target is unchanged from the original
  commit and separately included in the runner input hashes.
- Same reused `FlowValue.From(new { value = 42 })` message; input/output and terminal
  capacities 128; serial node execution; no installed listener/exporter.
- Three topology smoke cases per implementation, then one default representative
  run per implementation. Measured order per topology: original, current, current,
  original. Common invocation counts: 131,072 / 16,384 / 8,192 for the three rows;
  eight warmups, 15 actual iterations, unroll 16, one launch, no outlier removal.
- **12 measured processes, 180 actual iterations and 96 warmup iterations**.
  Including six dry processes and two default representatives: 20 successful
  benchmark processes in 16 qualified reports.

The clean reconstructed original build exposes four existing nullable diagnostics:
`FlowEvent.cs` lines 206/227/238 (CS8603) and 218 (CS8602). They were preserved and
explicitly allowlisted, not fixed or suppressed. Current runner builds have zero
warnings/errors. Initial failed verifier attempts remain saved and excluded: the
first expected a warning-free original build; the next selected dependency
manifests too broadly. Fresh smoke runs passed after correcting those checks.

Reproduction and evidence:
[plan](../../artifacts/core-refactoring/final-delivery-comparison-20260911/PLAN.md),
[original content verification](../../artifacts/core-refactoring/final-delivery-comparison-20260911/original-content-verification.json),
[run script](../../artifacts/core-refactoring/final-delivery-comparison-20260911/run.ps1),
[paired execution order](../../artifacts/core-refactoring/final-delivery-comparison-20260911/paired-order.json).
Preserve completed run directories; do not overwrite them to repeat an experiment.

The independent [evidence audit](../../artifacts/core-refactoring/final-delivery-comparison-20260911/audit.json)
rechecked all 16 qualified reports, 20 successful processes, 12 measured results,
180 actual/96 warmup iterations, generated binary identities, 1,488 source inputs,
summary numbers and the then-current 32 documentation links. No measurement was
rerun for that audit.

## Interpretation and disposition

The original output used a manual queue/semaphore pump and sent directly to each
target with native `SendAsync`. Current output uses the canonical delivery path.
For public native links it creates a per-offer source-callback bridge; the bridge
asks native `SendAsync` for a source so filtering, postponed consumption,
reservation and release can route callbacks through the original output.

This additional per-offer work is a source-backed contributor, **not an allocation
profile proving attribution of the entire delta**. See
[delivery branching](../../src/FluxFlow.Core/Nodes/FlowOutput.cs) and
[offer bridge](../../src/FluxFlow.Core/Nodes/FlowOutputOffer.cs).
Existing [native protocol tests](../../tests/Unit/FluxFlow.Nodes.Tests/FlowOutputTests.cs)
cover filtered nonmatches, postponed consumption, reserve/release/reoffer,
reservation surviving unlink, and independent duplicate-target offer identities.
Those tests were already in local qualification; they were not rerun or counted
as new tests in this measurement batch.

Retain the small correct bridge for this checkpoint. Do not remove callback
guarantees, reuse stale offer identities, or introduce a second partial protocol
solely to recover microbenchmark time. The regression remains visible in the final
tradeoff review; explaining one contributor does not automatically accept its cost.

Named application routes use direct target admission within the same canonical
delivery owner and do not need this public source-callback bridge. Their
[current condition measurements](2026-09-11-current-condition-costs.md) are a
different workload/path, not a replacement original-to-current comparison.

This batch does not measure saturation, larger payloads, request expiry/faults,
revision swaps, retained-state recovery or telemetry SDK overhead. Three
representative topologies are intentional; no full Cartesian matrix or arbitrary
application SLO is claimed. Continue only the outstanding requirements recorded
in the [performance coverage ledger](2026-09-11-performance-evidence-coverage.md).
