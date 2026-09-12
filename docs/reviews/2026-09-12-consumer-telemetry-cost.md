# Consumer telemetry cost

Status: representative installed-SDK off/on timing scope qualified. No production
changes, new solution-test count or new package candidate.

## Configuration and evidence

One source-linked update host, one echo component, native request/reply, public
and component input/output capacity 16, eight concurrent producers, scalar strings.
Each process performs 512 warmup requests followed by approximately ten seconds of
measurement. Both modes execute the same message construction, ActivitySource and
Information-level ILogger call sites. Enabled mode adds consumer-owned SDK 1.18.0:
AlwaysOn sampling, simple synchronous trace/log processors, formatted processing
logs, one-second cumulative metric export, and exporters that keep scalar counts
rather than individual records. There is no network backend or SDK in Core.

Four smokes and eight fresh measured processes pass: two repetitions per mode and
framework, second round in reversed framework/mode order. **4,471,467 timed replies**
passed exact value/trace/exchange, count, drain and once-disposal checks. Warmup is
excluded from timing/request totals and included in the final export oracles.

| Requirement | Evidence |
| --- | --- |
| Matched workload and successful delivery | `Consumer_telemetry_cost_preserves_requests`: identical call sites, eight nonempty producers, exact responses/lineage, histogram and processing totals |
| Enabled/disabled instrumentation is real | Same case checks root creation on every call; `Consumer_export_totals_match_work` and verifier check enabled totals and all-zero disabled signals |
| Actual SDK exports, not raw-listener counters | `Consumer_export_totals_match_work`: exact root/request/process/log totals, nondefault contexts and successful ForceFlush |
| Bounded measurement storage | Fixed histogram plus scalar exporters; no retained span/log lists; final metric batch contains four points in each measured enabled process |
| Reproducible source/build scope | Clean nonincremental solution/consumer builds; ten consumed DLLs equal current Release output; all 1,492 source inputs, five fixture inputs and 56 runtime files stable |

The solution build completed in 24.58 s with zero warnings/errors. The previous
4,864-pass regression remains the unchanged-source checkpoint; it was not rerun
in this batch and these console runs are not new discovered solution tests.

[Verification record](../../artifacts/core-refactoring/consumer-telemetry-cost-20260912/qualified/verification.json)
SHA-256: `C732C6B02D2E88F5063B3AA4A9FECC1EAD1DF36559232C5FE22A0014AB113013`.
It contains commands, asset/runtime hashes and all per-run results. See also the
[fixture](../../artifacts/core-refactoring/consumer-telemetry-cost-20260912/Program.cs)
and [assertion review](../../.testagent/consumer-telemetry-cost-review.md).

## Observed cost

Means below average the two processes per mode/framework. Latencies are ranges of
the fixed histogram's percentile upper bounds, not exact quantiles or confidence
intervals. Allocations include the fixture, typed projections, optional host and
configured SDK—not just Core transport. No cross-machine/application SLO is implied.

| Framework/mode | Mean requests/s | Mean bytes/request | p50 range (ms) | p95 range (ms) | p99 range (ms) |
| --- | ---: | ---: | ---: | ---: | ---: |
| net8 / off | 60,264 | 22,523 | 0.087–0.089 | 0.250–0.255 | 1.356–1.544 |
| net8 / on | 50,836 | 28,069 | 0.096–0.099 | 0.324–0.327 | 1.495–1.578 |
| net10 / off | 62,377 | 21,851 | 0.080–0.087 | 0.242–0.242 | 1.386–1.479 |
| net10 / on | 49,914 | 27,435 | 0.103–0.105 | 0.320–0.331 | 1.684–1.703 |

Relative to disabled mode, this consumer configuration reduces mean throughput
15.6% on net8 and 20.0% on net10, and increases allocation/request 24.6% and 25.6%
respectively. The on/off throughput separation exceeds the observed within-mode
spread, but two repetitions are not a statistical confidence claim. This is the
combined cost of three spans and one formatted processing log per request plus
metrics. It is not a breakdown by signal, a minimal SDK configuration, an original-
to-refactored comparison, or a reason to add buffering/collection machinery to Core.
Different consumer sampling, batching and exporters will have different costs.

## Diagnostic admission is not lossless metric sampling

All four enabled measured processes exported exact counts for each of the three
span kinds and processing logs: **2,019,288 per kind/category**, including warmup.
All had valid nondefault contexts. Disabled processes exported zero signals.

Runtime duration metrics are currently recorded after best-effort diagnostic
admission. Under load the bounded diagnostic path dropped events. The measured
enabled runs reported **6,921 diagnostic drops** and **2,543 fewer duration samples**
than requests including warmup. Drops also include non-duration diagnostic events,
so these totals are not a per-event attribution map. Per-run missing duration
samples were 33, 1,517, 907 and 86; each was bounded by its corresponding diagnostic
drop count. They are approximately 0.006–0.304% of that run's requests. Each enabled
process performed eleven metric exports and ended with four exported metric points.

This is an observability sampling limitation, not missing business replies or lost
trace/log exports. The request-duration histogram count must not be presented as a
lossless count of all requests. No production behavior was silently changed to
make the measurement pass. Existing signal-correlation and SDK-retirement reports
remain separate evidence; counter-only measurement does not prove full trace-tree
correlation or heap/owner collectibility by itself.

The representative SDK timing gate is complete. Remaining work is the final
requirement-by-requirement acceptance reconciliation, available real-service
integration and refreshed coherent local packages including HTTP/shutdown fixes.
No publication, external consumer coordination or backend installation occurred.
