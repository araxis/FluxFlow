# Core request lifecycle package qualification

The unchanged local Core candidate passed four smoke and eight ten-second measured
consumer runs on .NET 8.0.30 and .NET 10.0.11. Measured runs completed 21,525,789
requests with a maximum observed pending count of 16 and zero pending after drain.
Each run also passed 64 full-registry cancellation/recovery cycles and one graceful
shutdown-with-pending-work case. No library, permanent test or benchmark sources
were changed, and no external service or package publication was involved.

## Requirement evidence

The three named cases are explicit console-consumer checks, not newly discovered
unit tests. The artifact-local project references only the Core candidate package
and reuses the existing fixed-storage latency histogram source.

| Requirement | Concrete evidence |
| --- | --- |
| Concurrent shared-trace requests settle correctly | `Concurrent_requests_preserve_identity`: 16 producers reuse their own immutable original envelopes over one/eight hops; checks per-worker payload reference, message/trace/header identity, nonempty fresh correlation, exact per-hop processing totals and empty registry |
| Capacity, cancellation and recovery remain bounded | `Canceled_capacity_recovers`: 32 pending exchanges against eight native input slots, explicit 33rd-request rejection, all 32 tasks canceled with the caller token, zero registry, successful distinct-payload recovery, exact 576 processed messages across 64 rounds |
| Graceful stop handles active, queued and postponed requests | `Stop_settles_requests_and_drains_accepted_work`: 32 pending requests, all caller tasks settled on stop, new requests rejected, stop remains pending while accepted work is held, exactly eight accepted messages drain after release, successful graph completion and one owned disposal despite repeated disposal |
| Qualification uses the intended package and source | `qualified-final/verification.json`: 1,487 stable source inputs, 13 stable runtime files, candidate Core DLL hashes matched on both frameworks, all report hashes independently rechecked |
| Existing regressions and builds remain green | Full nonincremental Release solution build: zero warnings/errors; explicit consumer build: zero warnings/errors; 38 selected request/reply/tracing tests per framework, 76 passed and none failed/skipped |

There were 768 cancellation/recovery cycles across the twelve final runs: 24,576
canceled requests, 768 capacity rejections and 768 successful recovery requests.
The twelve shutdown cases separately canceled 384 caller requests and drained 96
accepted messages. Settling a caller request does not mean already accepted work is
discarded; the fixture distinguishes these outcomes explicitly.

## Timed workload and results

Each process warms up 64 requests, then runs 16 producers for ten seconds and joins
their final requests. Each producer reuses an original envelope with a worker-specific
payload, a common business trace, and a 256-character data string. Core supplies the
new exchange correlation. Every node is a native bounded echo wrapper; there is no
serialization, expression evaluation, external I/O, artificial target delay or
configured telemetry listener. Both input and output capacities are eight; the
application registry capacity is 32. Reported elapsed time includes final request
and processing drain but excludes graph construction, payload setup and disposal.

Two repetitions run in fresh serial processes per framework/topology, reversing hop
order in the second repetition. These are descriptive current-package measurements,
not a statistically established before-refactor improvement or an application SLO.

| Framework | Hops | Requests/second, first / second | p99 upper ms, first / second | Whole-process allocated bytes/request, first / second |
| --- | ---: | ---: | ---: | ---: |
| 8 | 1 | 290,462 / 307,091 | 0.635 / 0.601 | 1,284 / 1,282 |
| 8 | 8 | 216,468 / 219,712 | 0.615 / 0.656 | 2,792 / 2,799 |
| 10 | 1 | 331,422 / 336,368 | 0.851 / 0.869 | 1,254 / 1,254 |
| 10 | 8 | 222,716 / 228,237 | 0.656 / 0.649 | 2,780 / 2,793 |

The latency histogram covers individual completed requests and stores upper bounds
with 64 logarithmic buckets per doubling, not exact quantiles. Allocation and CPU
include the consumer checks and all process work; they are not pure library costs.
Raw reports retain p50/p95/p99, CPU, per-hop counts and heap deltas. The lifecycle
cases are handshake-driven correctness/stress checks outside the timed interval;
their cancellation and stop latency are not measured here.

Machine reports Windows 10.0.26200, X64, 20 processors and workstation GC. No
affinity, process-priority or power-plan changes were made. This task did not run
builds or other load tests concurrently with the final measured processes.

Forced-GC graph-alive heap deltas in measured runs were +807,200 to +824,336 bytes.
The initial 64-message warmup does not fill all bounded settlement-history capacity;
runtime and harness state can also remain alive. This experiment does not attribute
retention or prove a leak or leak freedom. No post-disposal heap claim is made.

## Review, reproducibility and limits

Artifacts: `artifacts/core-refactoring/request-lifecycle-20260911`.
`Program.cs`, `RequestQualification.csproj`, `NuGet.Config` and `verify.ps1` define
the consumer and checks. `qualified-final` contains all commands, logs, twelve raw
reports, report hashes, fixture/source/runtime inventories, two TRX reports and the
successful final verification. `PLAN.md` states the acceptance scope.

The initial `qualified` attempt passed behavior runs but failed its final inventory
loop: progress strings were captured into the binary list. The original script,
logs and results remain frozen with `DISPOSITION.md`. The corrected runner uses
the host output stream and a new output directory. Only `qualified-final` results
are included above; the first attempt is not pooled into the measurements.

The test-guidance review strengthened exact drain accounting rather than relying
only on an empty registry. `.testagent/request-lifecycle-review.md` records assertion
categories and hypothetical mutations; no mutation engine or coverage collector was
run. Static pairing identified the existing source/test pairs, not executed coverage.
This artifact-local console is intentionally not added to solution test discovery;
its explicit invocations are recorded separately from the existing unit-test count.

Remaining scopes include timed deadline-expiry/fault behavior, optional update-host
request routing and actual revision swaps, trace/export/log/metric combinations,
larger payloads, and observation-quiescent retained-state checks. Ordinary application
performance and simplicity remain the priorities; no competitive throughput goal or
consumer latency target is invented. The candidate remains
`0.0.0-local.20260911.resource.2`; overall refactoring acceptance is still in progress.
