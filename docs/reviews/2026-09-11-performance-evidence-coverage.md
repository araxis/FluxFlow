# Performance evidence coverage

Disposition: the [final acceptance reconciliation](2026-09-12-final-acceptance-reconciliation.md)
closes the approved representative performance scope. The historical gap lists
below describe their original checkpoints, not additional work to repeat. Preserve
the measured costs, scoped retirement claims and workload limitations. Final local
packaging and unavailable real-service integration remain separate gates.

Latest [consumer telemetry cost](2026-09-12-consumer-telemetry-cost.md) completes
the representative installed-SDK off/on timing gate. Four smokes/eight fresh
measurements: 4,471,467 timed replies; no source changes, clean builds, ten current
DLL copies and 1,492 stable inputs. The chosen SDK configuration costs 15.6–20.0%
throughput and 24.6–25.6% allocation/request. Span/log totals are exact; duration
samples can be omitted by best-effort diagnostic admission, with drop totals
explicitly reported. Do not reopen this as a requirement for every exporter/backend.

Latest: [loaded optional-host termination](2026-09-12-host-termination-load.md)
qualifies the representative stop/fault workload and fixes an error-observation
race in optional Engine. Eight measured processes: 5,137,288 timed replies followed
by 1,024 held terminal requests, 32 once-disposed/collectible components. Four
smokes, clean build, 4,864 local passes and 1,492 stable inputs. Source-linked
consumer DLLs match current builds; Engine differs from context.1, Core does not.
This is load followed by controlled queued termination, not every ingress race.
Earlier Engine measurements keep their original binary identities; final package
refresh remains required alongside the HTTP correction.

Source update: the [HTTP queue correction](2026-09-12-http-context-boundary.md)
follows the [MQTT context correction](2026-09-11-mqtt-context-boundary.md).
Both preserve Core binaries. Real loopback HTTP and queued context correctness now
pass on both frameworks, but this is not transport throughput evidence. Existing
startup.1 measurements remain attached to their original binaries. A new candidate
and transport-aware verification are required; do not silently relabel old results.

This reconciles the approved performance gates with inspected harness capabilities
and saved verification records. A count-limited correctness matrix is not a timed
performance matrix; an isolated operation benchmark is not sustained runtime load.
Historical measurements retain their original source/binary qualification and are
not silently relabeled as fresh measurements of the latest candidate.

Current [active-host revision retirement](2026-09-12-active-revision-retirement.md)
closes the selected stable/changed-address component-retirement scope: eight fresh
runs, 128 replacements, 2,176 replies and 64 removed-address rejections. Old probes
clear while the active host answers; all 136 components dispose once and clear
after shutdown. Four smokes/builds pass; 1,491 sources stable and ten package DLLs
match current builds. Bounded collection includes scheduling intervals. This is
not held-work replacement, sustained fault load or a universal retention proof.

Current [consumer observer retirement](2026-09-12-observer-retirement.md) now closes
the representative disposed-application/native-target and disposed-SDK ownership
scope: four fresh runs, 32 application lifetimes, 512 requests, all 128 application
probes clear while SDK is alive, and all 16 SDK probes clear after its disposal.
Two smokes and clean builds pass; ten consumed DLLs match current Release binaries,
and all 1,491 source inputs are stable. This is not timed SDK overhead or retirement
of replaced components while an application remains active.

Current optional-host successful update load is complete for the representative
scalar workload: four ten-second package-only processes completed 2,426,077 requests
and 48 compatible replacements. Every revision processed data, public queues drained,
all 52 components disposed once and all probes cleared after host disposal. Request
p99 upper bounds were 1.285–1.595 ms. Two final smokes and clean builds passed; ten
production DLL copies match startup.1 and 1,488 sources stayed unchanged. See
[live request load](2026-09-11-live-request-load.md). This closes that successful
update/post-disposal scope, not private registry counts, live-host retention,
incompatible changes, sustained fault load or installed-SDK ownership.

Current representative payload/topology repetitions are complete: the existing
direct load harness passed 12 smokes and 24 ten-second measured processes across
both frameworks, 67,640,294 completed messages and 282,755,940 exact target deliveries.
Six scenarios cover the three representative graph shapes plus both structured/
binary sizes at 32 hops/eight targets. Each scenario/framework has two opposite-
order repetitions. No payload-sized per-branch allocation growth was observed;
every target received the original envelope reference. All 1,488 sources and 102
runtime files stayed unchanged; 14 production DLL copies match startup.1. See
[payload/fanout repetitions](2026-09-11-payload-fanout-repetitions.md). This closes
the outstanding representative payload/topology repetition item, not the remaining
optional-host/telemetry scopes or an exhaustive Cartesian matrix.

Current direct expiry/fault/retention evidence: four repeated package-only startup.1
processes across both frameworks completed 32,768 deadline failures, 1,024 successful
recoveries and 8,192 terminally faulted requests, with exact admission/drain/disposal
accounting. All 1,536 retired payload/application probes became collectible across
96 measured checkpoints after asynchronous observation boundaries. Two smokes,
76 focused tests and clean builds passed; 1,488 source inputs stayed unchanged.
See [request expiry/fault/retention](2026-09-11-request-expiry-fault-retention.md).
This closes the representative direct scope, not optional-host or installed-SDK
observer retention. Whole-process heap variability is not a leak-freedom proof.

Current original-to-current evidence: 12 matched measured processes across three
representative standalone topologies (1/1, 8/2, 32/8 hops/targets), two processes
per implementation/topology in opposite pair orders. Exact startup.1 Core binaries
and 1,488 unchanged source inputs are qualified against isolated original source
reconstruction. Mean operation time increased 15.9–31.9%; allocation increased
20.9–79.6%. This completes this representative before/after comparison, with the
regression and native callback tradeoff explicitly retained. See
[original-to-current delivery](2026-09-11-original-current-delivery.md).

Current condition evidence: startup.1 now has 24 qualified condition cases across
net8.0/net10.0, two independent launches per case (48 measured processes), 24 dry
checks and two representative default runs. All 1,488 source inputs are unchanged;
five production dependencies per target match the package candidate in both the
host and generated process outputs. This closes current-binary timed coverage for
the existing 1/8/32-hop, two-target, scalar workload across all four condition
modes. See [current condition costs](2026-09-11-current-condition-costs.md).
It does not substitute for original-to-final or sustained-load evidence.

Latest update evidence: startup.1 now has four package-only controlled in-flight
revision runs, 128 compatible update attempts and 2,176 replies. These prove the
held-old-work/new-traffic interleaving and rejection/cancellation preservation;
they are not timed load. See 2026-09-11-startup-candidate-and-inflight-revisions.md.
Prior measured runs remain attached to their original candidate binaries.

Consumer SDK evidence now includes four package-only in-memory export runs on
startup.1, with 64 requests, correlated processing logs/spans, runtime logs/events,
and duration histogram totals checked against the event stream. This closes the
raw-listener-only collection gap for that workload, not network/backend export or
timed overhead. See 2026-09-11-consumer-signal-qualification.md.

| Evidence | Actual scope | What it does not establish |
| --- | --- | --- |
| payload-matrix-verification.json | 144 count-limited runs: topology/payload/framework/runtime combinations; exact envelope accounting | Timed throughput/latency matrix, repetitions, telemetry or conditions |
| payload-identity-timed-verification.json | Sixteen representative ten-second 64-KiB structured/binary runs, one run/cell; source checkpoint of 1,480 inputs | Repeated full matrix or before/after improvement |
| graph-condition-matrix-verification.json | 24 within-candidate measured rows; 1/8/32 hops, two targets, unconditional/native/document/configured methods on two frameworks; older 873-input checkpoint | All targets/payloads/adapters under sustained load; current-binary qualification; before/after change |
| load-corrected-verification.json and load-baseline-review.md | Eight-hop/two-target slow/fast load; 22 reports include two 30-second slow runs; 1,475-input checkpoint | Current runtime, long retention, actual swaps or shutdown with accepted work |
| tracing-load/measurements/verification.json | 32 smokes across four sampling modes; 32 timed direct off/recorded runs, four paired repeats, extreme graph shapes | Exporters, metric/log collection, sustained optional-host matrix |
| ambient-restoration/measurements/verification.json | Four smokes and 16 timed measurements; four alternating before/after pairs per framework for 32-hop/eight-target recorded direct execution; only Core DLL/PDB differed in that experiment | The entire refactor's performance delta, long retention or other telemetry modes |
| long-saturation-20260911 | Eight passed smokes plus eight passed 120-second runs across both frameworks, direct/update hosting and off/recorded tracing; eight hops, two targets, 64-KiB binary payload and a slow sink; 1,487 source inputs and 102 runtime files verified stable | One run/cell; not a statistical comparison, complete matrix, exporter or actual revision-swap workload |
| request-lifecycle-20260911/qualified-final | Four passed smokes, eight ten-second direct Core package runs, two repetitions per framework/topology (one/eight hops), 16 producers; each process adds 64 full-registry cancellation/recovery rounds and graceful stop with 32 pending requests | Small reused payloads and no installed listener; lifecycle cases are stress/correctness checks, not cancellation-latency measurements; no update-host swaps, expiry/fault load, long retention or before-refactor comparison |

Artifact references in the table are under artifacts/core-refactoring. The sustained
batch's verification.json and independent summary checks both passed. Its PLAN.md
states the exact workload and exclusions; the result interpretation is recorded in
2026-09-11-sustained-saturation.md. Completion applies to this batch, not every
performance acceptance gate.

## Source-backed gaps

The current --load options expose runtime, duration/count, hops, fanout, capacities,
producers, payload shape/size, slow target and tracing mode. They do not expose
condition evaluation, request/reply production, revision swaps, shutdown with work
in flight, metric/log collection or an exporter. LoadGraph starts update hosting but
never applies another revision. LoadRunner cancels producer admission, drains to
idle, measures heap while graph/storage remain allocated, then stops the graph.

ApplicationMessagingBenchmarks and ApplicationTopologyBenchmarks do exercise
request/reply operations. Their existence does not prove sustained concurrent
exchange load or timeout/cancellation retention; those need their own measured
workload and evidence. Correctness tests of those behaviors remain valuable but are
not performance measurements.

The separate request-lifecycle package consumer now provides representative direct
request load and repeated cancellation/recovery plus graceful-stop evidence, with
source and candidate identity checked. Its exact scope and limits are recorded in
2026-09-11-request-lifecycle-qualification.md; it does not add those options to the
general --load harness or qualify every request/telemetry/runtime combination.

## Final acceptance disposition

Completed representative request/revision/termination work includes direct expiry,
cancellation/recovery, compatible held-work swaps and rollback, successful
sustained updates, stable/changed-address retirement, and the new loaded stop/fault
scope. Preserve their exact limitations; do not reopen them for a complete matrix.

1. Telemetry requirements are reconciled with completed signal/correlation,
   transport-context, observer-retirement and installed-SDK off/on timing evidence.
   Preserve the report's configuration-specific cost and diagnostic-sampling limit.
   Network/backend delivery remains a consumer/environment concern; do not invent
   an exhaustive backend-installation or exporter-performance matrix.
2. Owner/observer retained-state requirements are reconciled with explicit
   observation quiescence. Direct deadline/fault payload retirement and disposed graph
   collectibility now have repeated evidence above. The consumer SDK/native-target
   disposal boundary is also qualified in the new report; do not reopen that
   completed scope. Selected live-host revision retirement is now also qualified
   above; do not reopen it for an exhaustive matrix. Do not equate those scoped
   probes, graph-alive heap deltas or sampled heap sawtooth with universal leak freedom.
3. Keep numerical application SLOs separate from library qualification. The user
   clarified that this engine targets ordinary applications, not online gaming or
   competition with actor frameworks, and has not specified rate, payload or p99
   targets. Prioritize simplicity, bounded resource use, reliable delivery and
   measured responsiveness; do not invent an SLO or justify complexity solely by
   peak-throughput comparisons. Separate measured library behavior from a future
   consuming application's latency and capacity requirements. Unspecified consumer
   targets are not a reason to block measurements or repeatedly ask for a number;
   bounded-state and meaningful-regression checks still apply.

No listed open gate is waived by a green package build. This document changes no
production policy, removes no feature and makes no release-readiness claim.
