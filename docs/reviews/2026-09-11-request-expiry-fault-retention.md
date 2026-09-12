# Request expiry, terminal faults and collectibility

The unchanged startup.1 Core package passed repeated real-deadline and terminal
fault workloads on both supported frameworks. Four measured processes completed
**32,768 expired requests, 1,024 successful recoveries and 8,192 faulted requests**.
Every retirement checkpoint reached zero live weak probes. This completes the
representative direct-Core expiry/fault and collectibility checks, not optional-host
revision, observer/exporter or universal leak-freedom acceptance.

## Requirement evidence

These are artifact-local console checks, not new discoverable unit tests.

| Requirement | Concrete case and verified outcome |
| --- | --- |
| Expiry includes postponed admission | `Deadlines_cancel_postponed_admission_and_recover`: 256 rounds/process, each with 32 pending requests, eight native input slots, one held handler, and explicit 33rd-request rejection; all 32 finish with their own deadline exception, not the safety waiter's timeout |
| Accepted work and fresh requests remain correct | Same case: zero pending while work remains held; release then recover with a distinct payload/message identity; exactly nine processed messages per round (eight previously accepted plus recovery), 9,216 total across measured runs; postponed expired sends cannot survive as extra processing |
| Terminal failure settles callers and ownership | `Fault_settles_requests_and_disposes_owned_graph`: 64 fresh graphs/process, 32 pending each; original exception identity on every caller and graph completion, stopped ingress rejected, zero pending, held work not emitted, disposal preserves a nonempty collection of expected errors; exactly 256 owned disposals despite repeated disposal calls |
| Retired state becomes collectible | `Retired_payloads_and_disposed_graphs_are_collectible`: 96 measured checkpoints, 1,024 expiry payloads plus 256 fault payloads and 256 disposed applications; zero live probes at all checkpoints, one collection attempt after each asynchronous observation boundary |
| Qualification is current | 1,488 source inputs unchanged; exact candidate Core DLLs; 13 unchanged consumer runtime files; six final report hashes verified; clean nonincremental solution and consumer builds, 76 focused existing tests passed, zero failed/skipped |

The two final smoke processes separately cover 1,024 expiries, 32 recoveries and
512 faulted requests. They are excluded from the four-process measured totals.
Focused tests overlap the existing 4,848-case qualification; do not add 76 to that
unique count. No production, permanent test, existing benchmark or package changed.

## Workload and observed latency

Each expiry round uses a fresh structured value with 65,536 ASCII data characters
plus small JSON fields, shared by its 32 immutable envelopes. Native input/output
capacities are eight and the pending registry capacity is 32. All requests share
one business trace; exchange correlation remains independent. Real deadlines are
100 ms; handshakes establish held processing. The 20-second safety limits detect
hangs and are not application SLOs. Each expiry process runs about 28 seconds.

Measured processes run serially in framework order 10, 8, 8, 10, following smoke
checks. No competing build or load was started during those processes. Release
builds use SDK 10.0.400, .NET 10.0.11 / 8.0.30, Windows x64, workstation GC.
No listener/exporter, external I/O, expression engine or artificial CPU work is
installed. No power-plan, affinity or priority setting was changed by this batch.

| Process | Timeout observation p99 upper ms | Fault-to-caller observation p99 upper ms | Fault-to-disposal p99 upper ms |
| --- | ---: | ---: | ---: |
| 10, first | 113.857 | 3.635 | 4.613 |
| 8, first | 113.857 | 2.864 | 3.796 |
| 8, second | 117.617 | 3.024 | 3.796 |
| 10, second | 113.857 | 3.755 | 4.766 |

Fixed-storage logarithmic histograms report **upper bucket bounds**, not exact
quantiles. Timeout timing starts before RequestAsync and ends when the caller
checks its timeout. Fault timing starts before Fault; callers are awaited/checked
after the fault trigger, so observations include validation/enumeration and request
continuation work. Disposal includes joining graph completion and cleanup; the
second idempotency check is outside that latency sample. Cold first-round costs
are included. Each process has 8,192 timeout samples, 2,048 fault-caller samples and
64 disposal samples. These are descriptive workload observations, not a runtime
ranking, pure-library cost, before/after comparison or production p99 guarantee.

## Retention interpretation

Expiry payload probes are checked while the application remains alive, after all
request tasks have joined and a distinct recovery message has drained processing.
Fault payload/application probes are checked only after disposal. Helpers return
weak references rather than retaining results/tasks/envelopes. The observer yields
asynchronously before forced collection so a completing helper can leave its stack.
Each final measured checkpoint needed one attempt; the maximum observation time
per process was approximately 25–26 ms, including the yield and forced collections.

256 expiry rounds create 8,448 settled exchanges including recoveries, crossing the
existing 4,096-key history capacity twice. Source review confirms that history
retains bounded keys/statuses, not outcomes. Whole-process post-collection heap
samples still vary, including after history saturation (roughly 3.7–5.8 MB across
these runs). They include runtime and fixture state; no monotonic plateau, leak,
or absence of every possible retained object is inferred from those heap values.
The stronger narrow result is that **all selected retired payloads and disposed
applications became collectible** in this workload.

There are no installed telemetry observers/exporters here. Their asynchronous
drain/flush and retention must be evaluated separately; business drain alone does
not prove observation quiescence in a configured consuming application.

## Evidence and review

[Summary and report hashes](../../artifacts/core-refactoring/request-failure-20260911/summary.json),
[fixture](../../artifacts/core-refactoring/request-failure-20260911/Program.cs),
[scope/plan](../../artifacts/core-refactoring/request-failure-20260911/PLAN.md),
[command runner](../../artifacts/core-refactoring/request-failure-20260911/run.ps1),
[final measured qualification](../../artifacts/core-refactoring/request-failure-20260911/measure-20260911-211245/verification.json),
[final smoke qualification](../../artifacts/core-refactoring/request-failure-20260911/smoke-20260911-211219/verification.json),
[assertion/pseudo-mutation review](../../.testagent/request-failure-review.md).

The testing workflow changed the fixture materially: initial synchronous collection
found one live payload and was replaced with explicit bounded asynchronous
observation; disposal assertions were strengthened to reject empty aggregates.
Both changes were followed by fresh runs. The initial failed smoke and earlier
successful weaker-fixture batch remain preserved with dispositions and are not
pooled into final measurements. Static source/test pairing is not coverage, and
no mutation engine was run.

Remaining work is tracked in the
[performance coverage ledger](2026-09-11-performance-evidence-coverage.md).
Do not reopen these direct cases simply to fill an exhaustive matrix. No release,
publication or external consumer coordination occurred; the overall goal is active.
