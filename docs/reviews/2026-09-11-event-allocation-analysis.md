# Event allocation attribution and capacity correction

The allocation traces identify diagnostic construction/encoding as the dominant
sampled allocation path in the deep revision-enabled workload. A four-line capacity
correction in `src/Core/FluxFlow.Core/Nodes/FlowEvent.cs` is implemented and verified.
It changes no public API, event schema, routing policy, observer behavior or features.

## Attribution evidence

The offline analyzer consumes the previously validated current-build traces. Its
converted-log results reconcile exactly with a separate raw-event pass: allocation
sample counts, total AllocationAmount64 weights and every type-group weight agree.
No lost events, truncation or missing allocation stacks are reported. Resolved
application frames coexist with unresolved native addresses; zero missing stacks
does not mean every frame has symbols.

| Trace | Samples | Weighted allocated bytes | Type groups |
|---|---:|---:|---:|
| Direct | 81,707 | 8,709,990,808 | 45 |
| Updates | 117,168 | 12,489,819,632 | 100 |

In the updates trace, stacks containing ReportActivity account for 73.64% of total
sampled allocation weight and 72.90% in the fixed 2–8 second subset. Stacks through
FlowEvent.ToMessage account for 43.33% overall. These inclusive percentages overlap
and must not be added. Using the first resolved application frame as a disjoint
partition, ToMessage accounts for 27.23%, ReadObject for 6.75% and TryGetFlowEvent for
4.23%. Dictionary entry arrays are the largest sampled type group (12.63%).

The direct path has a different profile: linked cancellation sources account for
31.50% of type-weighted allocation; the first-application-frame partition attributes
57.91% to FlowOutput.SendAsync. This is a separate investigation, not grounds to
remove cancellation or combine unlike runtime contracts.

Current source confirms the diagnostic path constructs an event, serializes its
canonical message, adds context headers, then decodes it for instrumentation. The
ToMessage body dictionary has nine known fields but previously grew from zero;
resolved stacks explicitly include Dictionary.Resize under ToMessage.

## Implemented correction

Pre-size the nine-field event body, three-field header map, converted value map and
measurement map. Keep every existing conversion, validation and copy. No caches,
new overloads, lazy event factories, alternate event representation or observer
short-circuiting were introduced. Eliminating the immediate encode/decode roundtrip
remains a distinct possible follow-up requiring its own semantics review.

The existing allocation probe source is reused unchanged. Matched runtimes differ
only in Core DLL/PDB; all other probe binaries are hash-identical. Three alternating
pairs per framework (12 fresh processes), each with 10,000 warmups and 100,000 calls
per scenario, produce consistent event-to-message results:

| Framework | Before bytes/encoding | After bytes/encoding | Saved |
|---|---:|---:|---:|
| net8 | 3,488.254 | 2,936.208 | 552.045 (15.8%) |
| net10 | 3,488.258 | 2,936.212 | 552.046 (15.8%) |

Header-copy/builder cases varied between some processes; their differences are not
attributed to this correction. This probe establishes allocation for the specified
encoding operation, not end-to-end speed, latency, allocation across every event
shape or retained-memory guarantees.

## Verification and provenance

- Full nonincremental Release solution build: zero warnings/errors.
- Selected local regression: 4,409 passed, zero failed/skipped, 79 reports.
- Discovery: 4,619 cases. No new tests; existing behavior assertions retained.
- 1,481 source inputs stable during verification; FlowEvent.cs is the only source
  change relative to the qualified before measurement.
- Analyzer: `artifacts/core-refactoring/host-allocation-trace/analysis/verified-results/allocation-analysis.json`.
- Probe and test proof: `artifacts/core-refactoring/event-capacity/verification.json`
  and `allocation-comparison.json`.

Initial tooling attempts are not hidden: offline restore needed SDK configuration
read access; the analyzer required an explicit local serialization dependency; the
first probe build exposed a net8 dataflow reference mismatch, corrected with the
existing matching binary before the qualified runs. Original probe results remain
under `before`. Initial TRX aggregation treated brackets in generated filenames as
wildcards; literal-path validation corrected that without rerunning the successful
build/test/discovery commands.

Raw traces include initialization, warmup and teardown. Allocation ticks statistically
weight the sampled allocation by bytes since the previous tick, not exact individual
object sizes. The central timestamp subset is a sensitivity check, not a proven
production-only window. Initial analysis output is retained; verified-results is the
raw/converted cross-checked report. No new traces or uploads were needed.

The full goal remains active. Whole-runtime performance/telemetry qualification,
remaining capability audit and a refreshed final candidate remain open. The earlier
81-package local candidate is preserved but predates this source change. Nothing
published and no external consumer coordination performed.
