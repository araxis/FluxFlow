# Core refactoring execution ledger

Execution authorized: 2026-09-08. Status: approved local refactoring goal completed;
publication and external consumer acceptance are separate.

## Latest checkpoint: integrated candidate (2026-09-12)

The environment and license prerequisite is resolved. Real-service execution
exposed a source-event shutdown dependency cycle; the final correction orders
production, control writers and event consumers using the existing dependency
sorter, with no Core change. The revised linked/unlinked event cases and the
existing file-watch regression pass.

Clean full build: zero warnings/errors. Full configured regression: 4,868 passed.
Final real integrations: 90 durable-input, 117 durable-output and one broker/HTTP
workflow case passed; in-process acceptance also passed. All 1,492 source inputs
and 218 test/runtime DLLs remained stable; 42 integration production copies match
the candidate. Real-service runs are net10.0 source suites, not both-target or
package-only server conformance.

Qualified `0.0.0-local.20260912.integrated.1`: 81 packages/symbols, 162 target DLLs
and consumer copies, 81 READMEs and 36 fresh package-only commands on both targets.
Core binaries and previous feeds are unchanged. See the
[integrated report](../reviews/2026-09-12-integrated-local-candidate.md).
The separately configured external endpoint smoke remains unrun; disposable real
services close the approved local integration scope. Deferred features remain
deferred. Nothing published, committed, tagged, pushed or sent to another task.

## Previous checkpoint: coherent local package candidate (2026-09-12)

Qualified `0.0.0-local.20260912.qualified.1`: all 81 packages/symbols, 162 current
target DLLs and consumer copies, 81 READMEs and 36 fresh package-only commands.
Both frameworks pass minimal/API, workflow/resource/health, separate-process
durable restart, context regressions and shutdown smoke checks. Current sources
remain the 1,492-input/4,864-pass checkpoint. Historical feeds are unchanged.
See [candidate report](../reviews/2026-09-12-qualified-local-candidate.md).
The remaining real-service integration prerequisite was checked again: configured
container-engine pipe absent, no external smoke opt-in or broker/database setting.
No license accepted. Local work is complete; request environment/licensing input.
No publication or external consumer coordination is authorized or performed.

## Previous checkpoint: final local acceptance reconciliation (2026-09-12)

The [reconciliation](../reviews/2026-09-12-final-acceptance-reconciliation.md)
maps all seven structural and eleven behavior requirements, credits completed
representative performance work and leaves only coherent refreshed packages and
real-service integration gates. The verifier confirms 1,492 current inputs,
81 preserved regression reports/4,864 passes and a fresh in-process workflow
acceptance pass with 13 production dependency copies matching Release outputs.
The local container engine is unavailable; external smoke opt-in, broker and
database settings are absent. No environment was started or license accepted.
No production source, package feed, external consumer or publication changed.
Proceed with local package qualification; request the integration prerequisite
decision once that remaining local work is complete.

## Previous checkpoint: consumer telemetry timing (2026-09-12)

Completed representative SDK off/on measurement without production changes:
four smokes, eight measured processes, 4,471,467 exact timed replies. Clean builds;
all 1,492 source inputs and fixture/runtime hashes stable, ten consumed DLLs equal
current Release output. SDK-on combined throughput cost 15.6–20.0%, allocation
increase 24.6–25.6% for the documented consumer configuration. Exact span/log
totals pass; best-effort diagnostic drops cause some duration samples to be absent
and are explicitly reported, not hidden as a lossless request metric. See
[telemetry cost](../reviews/2026-09-12-consumer-telemetry-cost.md). No new solution
test count, library SDK dependency or candidate. Remaining: final acceptance and
integration reconciliation, then refreshed coherent local packages.

## Previous checkpoint: loaded host termination (2026-09-12)

Fixed an optional Engine shutdown error-observation race: settled component faults
are retained alongside secondary drain errors, without another coordinator or Core
change. Three permanent cases include deterministic delayed observation; red/green
records preserve the rejected first fix. Final clean full build and 4,864 local
passes; 81 reports, 76 nonempty/five excluded, 1,492 stable inputs. Four smokes and
eight measured processes pass: 5,137,288 timed replies, 1,024 held terminal requests,
32 exact disposals/cleared probes. Only Engine changes in the consumed five-package
closure; source-linked qualification, not a refreshed package. See
[termination report](../reviews/2026-09-12-host-termination-load.md). Remaining work:
SDK timing, final acceptance/integration reconciliation and coherent candidate.

## Previous checkpoint: active-host revision retirement (2026-09-12)

Four smokes and eight fresh stable/changed-address qualification runs pass on both
frameworks: 128 replacements, 2,176 exact replies, 64 removed-address rejections,
136 once-disposed components. All prior probes clear while the current host stays
running and answers; all final probes clear after disposal. Collection includes
explicit scheduling intervals, not an immediate-unrooting guarantee. Fresh clean
solution/consumer builds; all 1,491 sources stable, ten consumed DLLs current-build
equivalent. No production changes or new solution-test count. See
[active-host retirement](../reviews/2026-09-12-active-revision-retirement.md).
Fault-load/SDK-overhead and final acceptance/candidate work remain open.

## Previous checkpoint: consumer observer retirement (2026-09-12)

Qualified the selected retirement boundary without production changes: four fresh
processes across both frameworks, 512 requests through 32 application lifetimes,
128 application/provider/component/native-target probes clear with SDK alive,
then all 16 SDK/tracer/meter/logger-factory probes clear after disposal. Two final
smokes and clean solution/consumer builds pass. All 1,491 source inputs and fixture
hashes stable; ten consumed DLLs match current build and context.1 packages. The
HTTP fix is outside this dependency closure and still needs a new package wave.
See [observer retirement](../reviews/2026-09-12-observer-retirement.md), including
preserved fixture corrections and explicit limits. No new unique solution test
count, publication or external coordination. Remaining acceptance stays active.

## Previous checkpoint: HTTP queued-context correction (2026-09-12)

Found and fixed cross-request trace inheritance in the optional request bridge.
An optional standard ActivityContext travels with each transport context; HTTP
captures its server operation and the bridge suppresses worker context. Three
production files, no Core/SDK change. Four permanent cases fail against frozen
context.1 and pass after correction. HTTP family: 26 passes. Clean full build:
zero warnings/errors; local regression: 4,861 passes, 76 nonempty plus five excluded
suites across 81 reports. Cross-target source fixture: ten cases each, including
real loopback HTTP on net8.0/net10.0. All 1,491 inputs stable; Core DLLs unchanged.
See [boundary report](../reviews/2026-09-12-http-context-boundary.md). Context.1 is
preserved but predates this correction. Batch remaining source-affecting checks
before preparing another candidate; no publication or external coordination.

## Previous checkpoint: coherent context-boundary candidate (2026-09-12)

Qualified `0.0.0-local.20260912.context.1`: 81 packages and symbol archives,
162 matching qualified assemblies and consumer copies, 81 current READMEs.
Ten all-package commands, twelve minimal-consumer commands and four MQTT-package
commands passed across net8.0/net10.0. Nine existing MQTT context cases passed on
each framework; no new unique test count. Broker forwarding is simulated.
All 1,490 source inputs remain at the 4,857-pass qualification checkpoint.
Ten MQTT-family DLLs changed from startup.1; Core and other assemblies are unchanged.
See [candidate report](../reviews/2026-09-12-context-candidate.md). Old feed preserved;
no publication or external consumer coordination. Overall acceptance remains open.

## Previous checkpoint: MQTT trace-context boundary (2026-09-12)

Fixed missing wire context propagation and leaked startup context in the MQTT
family. One internal helper and two call-site changes; Core/public API/dependencies
unchanged. Nine new cases include actual publisher/trigger queued processing and
binary payload preservation, using recorded broker forwarding. The family passes
130 tests; fresh full nonincremental build is clean and local regression passes
4,857 tests. Of 81 report files, 76 are nonempty and five are explicitly excluded
integration/acceptance suites, not integration passes. All 1,490 source inputs
remain stable through validation, and both Core DLLs still match startup.1.

See [boundary report](../reviews/2026-09-11-mqtt-context-boundary.md) for red/green
evidence and recovered interrupted-run accounting. Frozen startup.1 does not
contain the fix: next candidate must have a fresh identity and package-only
qualification. No repack, publication or external consumer coordination yet.

## Previous checkpoint: optional-host live request load (2026-09-11)

Four measured package-only processes completed 2,426,077 replies and 48 compatible
replacements across both frameworks. Every revision processed measured requests;
exact processing/reply totals, drain and once-only disposal passed. All 52 selected
component probes cleared after host disposal. Two final smokes and clean consumer/
full solution builds passed; 1,488 sources and ten candidate DLL copies qualified.
No production changes, repack or publication. See
[results](../reviews/2026-09-11-live-request-load.md) for local timings, allocation,
scope limitations and preserved superseded smoke attempts. The representative
successful-update load scope is closed; overall acceptance remains active.

## Previous checkpoint: representative payload/fanout repetitions (2026-09-11)

Executed the unchanged current load harness: 12 smokes and 24 ten-second measured
processes, two repetitions per scenario/framework. Measured 67,640,294 completed
messages and 282,755,940 target deliveries with exact reference identity/counts,
bounded tracking and empty queues/handlers/tracking after drain. Six representative
scenarios cover all three graph shapes and small/large structured/binary data at
32 hops/eight targets. No payload-sized per-branch allocation growth was observed;
allocation changes for 256x data size were −1.66% to +1.10%, with process variation
reported explicitly. See [results](../reviews/2026-09-11-payload-fanout-repetitions.md).

All 1,488 source inputs and 102 runtime files stayed unchanged; 14 production DLL
copies match startup.1. No new C# code, build, restore, repack or publication.
Representative payload/topology repetition is closed. Remaining optional-host/
update, observer/telemetry, cross-cutting and external acceptance remain active.

## Latest checkpoint: request expiry, fault and retired-state checks (2026-09-11)

Exact startup.1 Core passed four repeated measured and two smoke processes across
net8/net10. Measured totals: 32,768 expired requests, 1,024 successful recoveries,
8,192 faulted requests and 256 exactly-once owned disposals. All 1,536 selected
retired payload/application probes became collectible at 96 checkpoints. The
100-ms deadline workload has descriptive p99 upper bounds of 113.857–117.617 ms;
fault observation/disposal includes harness work, not a production SLO.

See [qualification](../reviews/2026-09-11-request-expiry-fault-retention.md).
Initial synchronous observation and a vacuous aggregate-error check were corrected
in the artifact fixture; earlier results remain separate. Final smokes, 76 focused
existing tests and clean solution/consumer builds passed. All 1,488 qualified source
inputs and candidate packages are unchanged. Representative direct failure/retention
work is closed; remaining optional-host/observer, payload and external gates remain.
No production/permanent-test/benchmark change, repack or publication. Goal active.

## Latest checkpoint: paired original-to-current delivery (2026-09-11)

Completed three representative standalone topologies against reconstructed original
source and exact startup.1 Core binaries: 12 measured processes, 180 actual and 96
warmup iterations, plus six dry processes and two representative default runs.
All 1,488 candidate sources stayed unchanged; 30 reconstructed source/build files
match original Git content after line-ending normalization. The original clean
build's four nullable warnings are preserved and disclosed, not hidden.

Both opposite-order pairs confirm the regression in every topology: mean operation
time increased 15.9–31.9%, allocation 20.9–79.6%. See
[original-to-current delivery](../reviews/2026-09-11-original-current-delivery.md).
The native per-offer callback bridge adds work and preserves protocol guarantees;
it is a contributor, not a complete profile-based explanation. Retain the correct
small bridge and keep the regression visible. Named application routing is a
distinct path, not substitute comparison evidence. No production/test/existing
benchmark or package changes. This representative before/after gap is closed;
remaining payload, lifecycle/retention and external gates remain. Goal active.

## Latest checkpoint: current candidate condition costs (2026-09-11)

Qualified the existing condition benchmark against exact startup.1 packages on
net8.0 and net10.0: 24 measured cases, two process launches each, plus 24 dry checks
and two representative default measurements. All 1,488 qualified sources remained
unchanged; all five production dependencies per target matched the candidate in
host and generated process output. A task-local package runner links the existing
benchmark body unchanged. Initial failed launch/restore/binary-identity attempts
remain preserved and excluded from qualification.

See [current condition costs](../reviews/2026-09-11-current-condition-costs.md) for
exact timings, confidence margins, allocations, per-launch results and limits.
Native predicates stayed close to unconditional delivery; expression adapters
carry explicit conversion/evaluation costs. No production/test/existing-benchmark
code or package changed. The outdated-source condition gap is closed; original-
to-final, remaining lifecycle/retention and external gates remain. Goal active.

## Latest checkpoint: remaining local family dispositions (2026-09-11)

All remaining ordinary component families and additional transport/request wrappers
now have source/replacement and assertion-level local behavior evidence. The fresh
27-project net10.0 batch passed 882 cases with zero failures/skips; 1,488 qualified
inputs stayed unchanged and 149 production dependency copies match startup.1.
Of 218 original runtime paths, 166 are unchanged, 33 changed and 19 removed; every
removed path has a current replacement/evidence mapping. See
[remaining-family qualification](../reviews/2026-09-11-remaining-family-qualification.md).
Together with the seven-family and storage reviews this finishes local family
dispositions, not cross-cutting/performance/external integration acceptance.
No production/test C# change, repack, publication or external coordination occurred.

## Latest checkpoint: seven component families reconciled (2026-09-11)

Serialization, Assertions, Payloads, Validation, State, Projections and Sessions
now have baseline-to-current runtime accounting and inspected operation assertions.
Of all 92 runtime C# files, 82 are unchanged; eight changes add direct port facts,
one preserves upstream error identity, and one consolidates assertion execution.
All 14 runtime/Composition suites passed 263 cases on net10.0. The initial verifier
misclassified a shared test helper as a package; that stopped qualification, was
corrected with explicit helper-reference/hash checks, and the full rerun passed.
All 1,488 qualified inputs stayed unchanged and 79 production dependency copies
match startup.1. See [family qualification](../reviews/2026-09-11-component-behavior-qualification.md).
No production/test C# edits or repack were needed. Remaining families and the
performance/integration/final acceptance requirements remain active.

## Latest checkpoint: authoring and optional behavior qualification (2026-09-11)

Reconciled actual assertions for constructor/document/fluent execution, link and
output policy, scoped/native evaluator configuration, editor lossless persistence
and readiness during/recovering from updates. Eight fresh net10.0 suites passed
666 cases, zero failures/skips. All 1,488 qualified source inputs stayed unchanged;
all 32 loaded FluxFlow dependency copies match startup.1. No production/test C#
changes or repack were needed. Evidence and specific closed checks are in
[the behavior qualification](../reviews/2026-09-11-authoring-behavior-qualification.md).
These executions overlap the full local regression, not additional unique cases.
Remaining family, performance and real-integration requirements remain active.

## Consolidated acceptance index (2026-09-11)

The seven-part handoff and finite remaining checklist now live in
[the acceptance index](../reviews/2026-09-11-refactoring-handoff.md). It ties the
current startup.1 candidate to its API example, package map, migration accounting,
exact test scope and historical performance caveats. The ordinary-application
performance clarification adds no arbitrary SLO or actor-framework competition
requirement. Remaining work is behavior reconciliation, approved performance
qualification, real integrations with explicit prerequisites, and final candidate/
consumer handoff. This documentation-only consolidation changes no source or feed
and does not claim the still-open acceptance gates have passed.

## Latest checkpoint: persistence migration qualification (2026-09-11)

Verified all 53 baseline C# files across six persistence providers: 43 unchanged,
ten reviewed addressing-namespace relocations, no added/removed provider C# files.
No selected provider files were dirty in the captured starting worktree. Reviewed
canonical input restoration, output capture and the four direct storage wrappers.
Nine current suites passed 766 cases on net10.0, including actual embedded/file
stores and inherited lease/replay/retention conformance. Exact capability-to-test
mapping is in docs/reviews/2026-09-11-persistence-migration-qualification.md.

All 1,488 qualified source inputs stayed unchanged; 22 persistence package target
DLLs match startup.1. The public component matrix now identifies the current
candidate and storage evidence. No library change or repack was needed. A read-only
check still finds no container-engine pipe or configured external database, so
server-database runtime conformance remains unrun. Nothing published; goal active.

## Latest checkpoint: consumer SDK signal qualification (2026-09-11)

Added a package-only consumer proving actual SDK collection/in-memory export of
request and processing spans, correlated component logs, runtime logs and metrics,
alongside native diagnostic events. Four runs across net8/net10 passed: 64 requests
with exact span/log/event counts, parent identities, bounded metric labels and
duration totals matching original events. Fresh nonincremental solution/consumer
builds have zero warnings/errors. All ten consumed FluxFlow target assemblies match
the five qualified packages; all 1,488 source inputs and 58 runtime files stayed
unchanged. No library dependency or local candidate refresh was required.

See docs/reviews/2026-09-11-consumer-signal-qualification.md. SDK dependencies are
consumer-only. Network collector/backend delivery and remaining performance,
transport/failure and feature-level gates are not claimed complete. Nothing was
published or sent to another task; the goal remains active.

## Latest checkpoint: component constructor and configuration accounting (2026-09-11)

Compared all 60 captured public components with the qualified startup.1 consumer:
56 current concrete types, 58 unchanged constructor mappings, two reviewed
constructor changes, and no missing captured public port names. Of 178 original
component options/contracts files, 176 are unchanged; the two request/reply
exceptions have explicit migration dispositions. Eight focused direct HTTP/bridge
tests passed, with all 1,488 qualified source inputs unchanged. No library edits
or package refresh were needed. The audit script's empty-array counting defect
was corrected and its entire inventory independently regenerated.

See docs/reviews/2026-09-11-component-contract-migration.md. This closes bounded
signature/configuration accounting, not every processing behavior or remaining
release gate. Performance expectations remain ordinary-application measurements,
without an invented throughput or latency SLO. The goal remains active.

## Latest checkpoint: corrected local candidate and in-flight updates (2026-09-11)

Built startup.1 from the qualified startup-transaction source. All 81 packages,
81 symbols, 162 target DLLs and 81 READMEs verified, with coherent internal version
coordinates. Ten all-package and twelve minimal-consumer commands pass across both
frameworks, including separate-process durable seed/recovery. Current source state
remains the 1,488-input, 4,848-local-pass checkpoint. Fresh full solution/consumer
builds are clean and the additional authored acceptance case passes.

Four final package-only Revision_changes_preserve_inflight_requests runs passed:
128 compatible update attempts (64 applied, 32 rejected, 32 canceled), 2,176 replies,
exact processing sets and once-only disposal of 264 component instances. Successful
new traffic is verified while old accepted work remains held; rejection preserves
the original routing. No arbitrary timing sleeps or private registry count claims.
This is controlled stress/correctness, not sustained throughput measurement.

Earlier restore-permission and fixture-teardown/build-lock failures are preserved;
the final complete batches passed. The source and package inventories still match
after qualification. See docs/reviews/2026-09-11-startup-candidate-and-inflight-revisions.md
and local-candidate-20260911-startup/acceptance.json. No publication or external
consumer coordination. Broader feature/performance/provider/final-deliverable gates
remain active; previous package feeds stay frozen.

## Latest checkpoint: candidate startup transaction (2026-09-11)

Fixed a confirmed update rejection bug: stable ports committed before candidate
source startup; a later startup failure left the application snapshot on the old
revision but routing on the failed candidate. Startup now occurs within the existing
gated/staged precommit boundary, with validation before/after and lifetime fault on
failure. Three internal Engine files, net +17 lines; no Core/API/dependency change.

Two original regression cases reproduced the defect. Five new cases plus a
strengthened fallback preflight test pass; all 531 Engine tests pass. The initial
full build passed but its regression found one declaration-baseline mismatch for
the changed member of an internal type. Reviewed single hash update, no declaration
count change. The failed batch is frozen; qualification-final passed: clean full
nonincremental Release build, 4,848 selected local tests across 81 reports with no
failures/skips, 5,058 discovered cases and successful sample. All 1,488 qualification
inputs stable; final TRX totals independently summed.
See docs/reviews/2026-09-11-candidate-startup-transaction.md and .testagent review.

The prior resource.2 local package is unchanged and lacks this repair. Fresh package
qualification and actual in-flight revision load remain required. No publication or
external consumer coordination. Performance work remains application-oriented;
no numerical SLO or actor-framework throughput competition is introduced.

## Latest checkpoint: Core request lifecycle package qualification (2026-09-11)

Added an artifact-local consumer of the unchanged resource.2 Core package, not new
library abstractions or solution unit tests. Four smokes and eight ten-second runs
passed on both frameworks over one/eight hops with 16 concurrent shared-trace
callers. Measured completions: 21,525,789; observed pending peak 16, zero after drain;
payload ownership, message/trace/header identity, fresh correlation and exact
per-hop processing asserted. Two repetitions per framework/topology are descriptive,
not before-refactor comparisons. This reused small in-memory payload workload had
p99 histogram upper bounds of 0.601–0.869 ms, not an application latency promise.

Each of twelve final processes also verified 64 cancellation/recovery rounds at
32 pending exchanges against eight native input slots, plus graceful stop with
active/queued/postponed work. Totals: 768 recoveries, 24,576 canceled requests and
768 capacity rejections; twelve shutdown cases separately settled 384 requests,
drained 96 accepted messages and disposed each owned component exactly once.
These lifecycle checks are outside the timed interval, not latency measurements.

Final full nonincremental solution and consumer builds: zero warnings/errors.
Selected existing request/reply/tracing tests: 38 per framework, 76 passed without
failures/skips. All 1,487 qualification sources and 13 consumer runtime files stayed
stable; Core DLLs match the candidate. No production, test or benchmark source edit.
Initial runner inventory failure is preserved; corrected qualified-final batch
fully reran and passed. See docs/reviews/2026-09-11-request-lifecycle-qualification.md.

Remaining scopes include optional-host swaps, fault/expiry load, relevant telemetry
and payload combinations, retained-state evidence and final broader acceptance.
The package candidate is unchanged; no publication or external consumer coordination.

## Latest checkpoint: sustained saturation and performance scope (2026-09-11)

Completed eight smoke runs and eight serial 120-second production runs on the
unchanged resource.2 candidate, crossing direct/update hosting, tracing off/recorded
and both target frameworks. Workload: eight hops, two targets, capacity 16, four
producers, 64-KiB binary payload and a deliberately slow target. All 65,011 measured
envelopes reached both targets exactly once with original reference identity.
Tracking stayed within bounds; all pending work and active observations drained to
zero. The batch and independent summary verified report hashes, known payload,
1,487 source inputs and 102 stable runtime files. No library or harness edits.

Direct tracking plateaued at 283–284 and updates at 682–684. Saturation p99 histogram
upper bounds were 4.52–4.57 seconds direct and 10.88–11.00 seconds with update
hosting, illustrating the latency cost of additional buffering. About 64 messages/s
is this slow target's effective service rate, not maximum engine throughput.

See docs/reviews/2026-09-11-sustained-saturation.md and the performance-evidence
coverage review. Explicitly separated historical count-limited, microbenchmark,
isolated fix and sustained-load evidence; none is relabeled as a full current-build
performance matrix or before-refactor speedup. User clarified ordinary-application
priorities, not gaming or actor-framework competition; no numeric SLO supplied.

This batch does not exercise request exchanges, actual swaps, shutdown under active
work, exporters or long-term observation-quiescent retention. Those and other final
acceptance/handoff gates remain open. No packages changed or published; goal active.

## Latest checkpoint: portable resource boundary and corrected candidate (2026-09-11)

Verified that resource documents already execute through optional Composition's
services overload over a plain FlowApplication, without Engine or Designer. Its
existing tests assert resource creation, native ports, transformed reply, ownership
and ordered cleanup. Corrected the direct Json error and README, which incorrectly
described this as unfinished migration. The lightweight constructor-binding path
still requires empty Resources and introduces no resource container or new API.

Added Resource_documents_require_composition_before_any_binding_runs: four cases
crossing string/document entry points with one/two resource declarations. They
assert the specific error, actionable supported paths and zero binding invocations.
Final wording says to leave Resources empty, preserving the required JSON property.
The first wording candidate is frozen and explicitly superseded before consumption.

Final qualification: clean full nonincremental Release build, 4,843 passing local
tests, 5,053 discovered cases (four new), passing fluent sample and one additional
in-process authored acceptance case. No failures/skips within these selected runs.
The filtered local suite still excludes real container/external/licensed suites.

Current candidate: 0.0.0-local.20260911.resource.2 in
artifacts/core-refactoring/local-candidate-20260911-resource-final/feed. All 81
packages/symbol archives, 81 readmes and 162 target/consumer assemblies verified;
ten all-package and twelve minimal consumer commands pass on net8/net10, including
separate-process local durable recovery. All 1,487 qualification inputs remain
stable. Only the Json target DLLs differ from t1430; runtime DLLs are unchanged.

See docs/reviews/2026-09-11-portable-resource-boundary.md and candidate acceptance.json.
The metadata-free path cannot preflight actual port existence before obtaining a
component's native port map; failed construction disposes owned instances without
starting sources. This limitation is explicit, not hidden by duplicate metadata.
Broader capability/member migration, telemetry/performance, real integration and
final handoff remain open. Goal active; nothing published or sent to another task.

## Latest checkpoint: baseline source removal accounting (2026-09-11)

Completed the file-level removal inventory for all missing baseline source paths:
83 rows, comprising 81 tracked files and two initially untracked condition files.
Every row identifies current replacement paths or an explicit retirement, hashes,
and evidence navigation. Eight files had captured starting changes, which are not
treated as identical to the starting commit. Eighteen replacements are exactly
identical under Git line-ending normalization. No original-path row is unmapped.
This accounts for baseline src removals, not transient files or member deletions
inside surviving source files, and is not automatic behavioral-parity approval.

Fresh affected-suite execution passed 415 tests: transport 121, observability 25,
routing 98 on each of net8.0/net10.0, file system 51 and host request trigger 22.
No failures/skips; these overlap the existing qualified regression count. Inspected
assertions cover direct native ports, request/reply, bounded admission, fanout
identity, acknowledgement/control drain, transport validation and cleanup.

All 1,486 qualified source inputs, 81 local candidate archives and 162 target
assemblies remain unchanged. No production/test/package changes or new deletions
were made. See docs/reviews/2026-09-11-source-removal-audit.md and
artifacts/core-refactoring/deletion-audit-20260911/verification.json. Remaining
gates are capability/public-member migration, telemetry/performance, available real
integration evidence and final handoff; no publication or external coordination.

## Latest checkpoint: current capability/type accounting (2026-09-11)

Reconciled the captured 62-package/60-public-node baseline with the current
81-package/56-public-node assemblies. All 60 original node types have a current
counterpart; four generic retry/timer variants consolidate into canonical nodes.
All 56 current public nodes implement IFlowComponent, expose public constructors
without activation/service-provider/creator dependencies, and use canonical
standard port types. No constructors or getters were invoked by the inventory.
Core has zero outward project references; all 19 ordinary composition packages
have zero transitive Designer project references. These are structural facts,
not proof of constructor side effects or complete behavior preservation.

Added all-package dependency/test-report navigation and all-type constructor/port
metadata under capability-audit-20260911. Corrected the stale public component
coverage matrix, which still named retired packages and described Designer as a
composition dependency. Source and candidate identities remain unchanged.

Fresh tests: timer suite 77, retry suite 35 and separate authored workflow
acceptance 1; all net10.0, zero failed/skipped. Timer/retry counts overlap the
qualified unit suite and must not be added as new unique cases. The acceptance
fixture uses a supplied MQTT message and in-process HTTP handler. Read-only
container checks, including approved access to the configured runtime, found
no active daemon pipe; real broker/socket integration remains unrun.

See docs/reviews/2026-09-11-capability-migration-audit.md and its verification.json.
The package/type inventory is complete for its stated scope; individual capability
assertions, file-level deletion/replacement evidence and full performance/telemetry
signoff are not. Use the current rows to close those substantive gates instead of
repeating unchanged build/package qualification. No publication or external work.

## Latest checkpoint: local candidate refreshed after output-policy fix (2026-09-11)

Candidate 0.0.0-local.20260911.t1430 contains all 81 manifest packages, 81 symbol
archives and the current package readmes, with a coherent local dependency version.
All 162 target DLLs and symbols match the qualified Release outputs; all 162 copies
loaded by the full-package consumers match those archives. Source checkpoint stays
at 1,486 inputs, linked to 4,839 local passes and 5,049 discovered cases. A fresh
nonincremental build also passes with zero warnings/errors after consumer runs.

Ten full-package consumer commands pass on net8/net10, including representative
JSON/code-first/resource/health/fluent/storage and separate-process restart tests.
Four minimal consumers pass twelve commands with exact dependency closure: Core
only internally, Fluent adds only Core; the older target also needs the native
dataflow package while the newer target uses the runtime-provided assembly.

The minimal Core fixture exposed an existing attachment race: it sent input before
listening to a live non-replaying output. Original timeout and SDK permission
failures are retained. Only the new artifact-local fixture was repaired, by
linking a bounded target before input and checking extra output/completion; Core
is byte-identical to t1400. Twenty additional executions of the corrected Core
fixture pass. No production change, weakened assertion or extended timeout.

See docs/reviews/2026-09-11-local-candidate-output-policy.md and the t1430
acceptance.json. Earlier feeds remain untouched. This completes local candidate
refresh, not per-module behavioral, external provider, full telemetry/performance
or overall architecture acceptance. Continue the approved capability/evidence
ledger and remaining substantive gates; no publication or external coordination.

## Latest checkpoint: explicit direct Fluent output policy (2026-09-11)

Fixed the execution-owner audit's concrete mismatch. Build no longer marks all
unlinked outputs for discard or tracks a redundant linked-output set. To/Tap
explicitly terminate their selected component's outputs; DiscardUnmatched selects
only the current continuation/branch. All policies use existing Core endpoints,
preserve explicit links and support input-only sinks. No processing queue or
runtime implementation was added. Public declaration baseline, changelog and
Fluent migration guidance updated within the unpublished preview train.

Three regressions failed before production edits because missing routes silently
succeeded. Fresh full local verification now passes 4,839 cases across 81 reports,
zero failed/skipped, including all 11 new policy cases. Discovery: 5,049 cases;
Fluent: 39 passing cases. Full nonincremental Release build: zero warnings/errors;
existing Fluent sample passes. All 1,486 qualification inputs remained stable.
See docs/reviews/2026-09-11-fluent-output-policy.md and the fluent-output-policy
artifact directory. Static pairing and assertion/gap review were used; no runtime
coverage percentage or measured performance improvement is claimed.

Frozen t1400 packages remain unchanged and predate this fix. Refresh the local
candidate from this qualified source before claiming package parity. Broader
capability, provider, telemetry and performance gates remain open. No publication
or external consumer coordination; context-aware Add remains deferred.

## Latest checkpoint: execution-owner trace and policy mismatch (2026-09-11)

Traced direct, Fluent, minimal document, contract/resource document, ordinary hosted
and update-enabled paths. Direct and hosted output dispatch reach the same Core
FlowOutput delivery batch; application request/reply uses the same bounded Core
coordinator implementation. Update hosting still owns additional bounded stable
input/output and prepared-output staging boundaries, plus revision drain/snapshot
policy. These costs and lifetimes are explicit, not hidden by a nominal wrapper.

Fresh selected tests: 231 pass, zero failed/skipped (Core 67 per framework,
Composition 31 and Engine 66 on net10). Existing assertions verify capacity,
held-route snapshots, capture/abort boundaries, shared lifetime failure, retired
output observers, direct resource ownership, fanout identity and request/reply.
See docs/reviews/2026-09-11-runtime-execution-owners.md and the runtime-owner-audit
artifact directory. Core/Engine/Fluent readmes now describe current execution
ownership instead of historical incomplete-dispatch wording. No production C#
changed; all 162 qualified assemblies and 81 candidate archives remain unchanged.
The frozen t1400 feed retains older readmes; later candidate refresh must include
these documentation changes and any subsequent fixes.

Actionable next implementation: FlowGraphBuilder.Build currently discards every
unlinked output, including accidentally unconnected sources/branches. This differs
from the approved explicit-discard/default-failure contract and is not covered by
the passing connected Fluent sample. Preserve intentional terminal/tap/branch
shapes while making discard intent explicit; add a real regression before changing
the builder. Do not add another router or silently call parity complete. Remaining
capability/provider/telemetry/performance gates stay open; no external actions.

## Latest checkpoint: minimal Core and Fluent consumers verified (2026-09-11)

Four fresh package-only consumers execute the current t1400 candidate on net8/net10.
Core's complete package closure is Core plus Dataflow on net8, Core only on net10
where Dataflow is runtime-provided. Direct Fluent adds only Fluent to those sets.
No host, composition, document, designer, evaluator or provider package is required.
All internal restored archives and runtime DLL copies match the candidate.

Three new artifact-local acceptance methods pass on both frameworks: standalone
processing, named cross-workflow conditional fanout with original envelope identity
and owned cleanup, and eight overlapping shared-trace requests with exact replies
and no pending state. The unchanged Fluent sample passes exact linear/branch output
checks twice. Twelve consumer commands succeed; all four builds and the full fresh
nonincremental solution build have zero warnings/errors. The initial incorrect
net10 package-dependency expectation is retained as failed verifier evidence.

Sources remain at the 1,485-input qualified snapshot; all 162 target DLLs/symbols
still match the candidate after rebuild. No new discovered unit cases or full
regression rerun is claimed. See docs/reviews/2026-09-11-minimal-package-boundaries.md
and minimal-consumers-20260911/final-verification.json. Minimal package qualification
is complete; next audit actual execution ownership/capability migration and stale
documentation. Wider performance, telemetry/operation/provider gates remain open.
No publication or external coordination.

## Latest checkpoint: refreshed local packages accepted (2026-09-11)

Candidate `0.0.0-local.20260911.t1400` contains all 81 manifest packages and 81
symbol archives. All internal dependencies share its version. Sources remain
identical to the 1,485-input ambient-restoration qualification (4,828 local passes,
5,038 discovered); no production, test or acceptance-fixture source changed.

Fresh package-only consumers restore all 81 packages on each supported framework,
using isolated caches and archive identity checks. Both builds have zero warnings
and errors. All ten restore/build/run/seed/recover commands pass, including actual
separate-process durable restart and exact idempotent destination effects. Final
verification matches all 162 packaged DLLs and symbols to qualified binaries and
all 162 consumer DLL copies to the same inventory. Existing full tests were not
rerun; the unchanged qualified snapshot provides provenance.

See docs/reviews/2026-09-11-local-package-refresh.md and the corresponding
local-candidate-20260911-1400 final-verification.json. The older candidate remains
preserved. This closes the current fresh-package gate, not every module's behavior,
minimal-consumer boundaries, full performance/retention, provider prerequisites or
final capability/documentation acceptance. Core readme historical routing wording
needs review. No publishing or external coordination occurred.

## Latest checkpoint: ambient restoration fixed and measured (2026-09-11)

The existing helper now clears a saved activity already stopped instead of asking
the runtime to restore it. Live saved context and callback isolation remain intact.
One guarded assignment/comment; no API, dependency, scope or baseline change.
New regressions first fail nine stopped cases per runtime, then all 202 processing
trace cases pass. Final clean nonincremental build: 4,828 local passes in 81 reports,
5,038 discovered cases including all 36 additions, 1,485 stable qualification inputs.

Four count checks and sixteen timed deep full-recording workloads pass, four
alternating pairs per framework. Only Core DLL/PDB differ; all 244 runtime-file
entries remain stable. Paired median throughput rises 453.02% on net8 and 104.10%
on net10, allocation falls 30,891 / 8,553 bytes per completion, and p99 upper bounds
fall. All span/parent counts, delivery and drain invariants remain exact. This is
a verified bounded improvement, not whole-goal performance acceptance. See
docs/reviews/2026-09-11-ambient-restoration-fix.md and ambient-restoration manifests.

Next qualify remaining current-runtime telemetry/load/retention and operation
boundaries, then refresh coherent local packages. No publishing or external
coordination. Host-concurrent stop/check races are not claimed to be atomic.

## Latest checkpoint: recording cost traced to invalid restoration (2026-09-11)

All four focused traces convert and reconcile raw/converted allocation weights,
types and counts, plus thread sample types/counts. No loss/truncation or empty
sample stacks; unresolved frames stay visible. RestoreCurrent accounts for 11.44%
/ 13.03% of sampled allocation by first application frame on net8/net10, with
Activity.NotifyError prominent in thread stacks. These are not exact CPU shares.

A direct public-scope reproduction against frozen Core confirms that restoring a
stopped activity raises one internally swallowed exception on each runtime; a live
saved activity restores by reference with zero errors. Next add regression coverage
and guard the existing helper against finished saved activities, preserving valid
parent restoration and tracing/delivery. Then qualify and remeasure against the
frozen baseline. No production/API/harness/test change in this diagnostic slice.
Inputs/runtime hashes remain stable; prior regression was not rerun. See
docs/reviews/2026-09-11-recording-stack-analysis.md. Goal remains in progress.

## Latest checkpoint: focused recording traces captured (2026-09-11)

Four focused traces (off/recorded on net8/net10) contain allocation and thread
samples with zero reported loss. Core, Engine and harness symbols match on both
frameworks. All eight initial/retry workload processes pass delivery/scope/drain;
1,485 inputs and 122 runtime files remain stable and runtime copies are frozen.
The initial broad capture set is not qualified: its recorded net8 trace failed
the zero-loss gate. Initial captures and reader failures are preserved separately.

No production/API/harness/test change; the prior 4,792-pass qualification is not
rerun. Current evidence: recording-trace/focused-results collection and validation
manifests, metadata, symbol matches and raw artifacts. Next validate converted
stacks and attribute sampled allocation/managed-thread cost before optimizing.
Capture quality is not performance acceptance. See
docs/reviews/2026-09-11-recording-trace-capture.md. Broader gates and fresh packages
remain open; no publishing or external coordination.

## Latest checkpoint: recording observer isolation (2026-09-11)

A dependency-free artifact-local probe compared full recording with only a sample
counter against the existing full counter observer. All four count checks and
sixteen timed workloads pass on both runtimes. Four alternating pairs per runtime
retain 1,485 unchanged qualification inputs, 122 unchanged runtime files, probe
input/assembly hashes and exact delivery/drain. No production, API or permanent
harness change; the prior 4,792-pass qualification remains applicable, not rerun.

Reduced-observer paired throughput medians: -6.05% on net8, +4.99% on net10.
This does not support blaming the much larger recording cost on the removed
callbacks alone. One shared sample counter remains; no zero-observer or pure Core
cost attribution is claimed. No exporter or new trace collection in this slice.
Next profile current deep recording CPU/allocation paths; performance is still
unaccepted. See docs/reviews/2026-09-11-recording-observer-isolation.md and
artifacts/core-refactoring/counter-isolation/measurements/verification.json.
All broader gates below remain open; no publishing or external coordination.

## Latest checkpoint: current tracing load cost (2026-09-11)

The bounded-load harness now has explicit listener modes and optional fixed parent,
with counter-only observation, exact expected scope totals and parent/drain validation.
No production code, public API or baseline changed. Full clean Release build passes;
4,792 local cases pass in 81 reports; discovery finds 5,002 including 21 additions;
1,485 qualification inputs remain stable. Focused load tests pass 67 cases.

All 32 direct/update four-mode smoke runs and 32 direct timed off/recorded runs pass
on both frameworks. Four alternating pairs per framework/shape retain exact payload,
delivery and drain, with 1,485 source and 122 runtime files unchanged. All sixteen
pairs show lower throughput and higher allocation with full recording. Paired rate
medians: one-hop -35.17%/-29.74%, deep -92.40%/-79.57% on net8/net10. This includes
shared atomic counter callbacks, not just Core activity overhead. No exporter/backend.

Performance is NOT accepted. Next isolate observer contention from activity/context/
tag cost and profile the deep path before choosing optimizations. Preserve tracing,
immutable ownership and delivery. See artifacts/core-refactoring/tracing-load and
docs/reviews/2026-09-11-tracing-load-measurement.md. Longer retention, broader workload
matrix, remaining operation boundaries and fresh local packages remain open.
No publishing or external coordination.

## Latest checkpoint: error forwarding outcomes (2026-09-11)

FlowNode error bypass now shares its existing exception-tracing handlers, while
rethrowing forwarding faults to preserve terminal behavior and avoid replacement
error data. Successful forwarding still preserves the exact input message without
calling the handler. Explicit error-handling components keep their prior contract.
No additional scope/helper, public API or dependency; no baseline adjustment.

New tests reproduce and fix absent error/canceled outcomes during blocked forwarding.
Fifty-four cases cover bypass/handling, off/None/propagation/full sampling, drain/
owner fault/decline and throwing callbacks. All 166 processing-trace cases pass,
with 162 new-case executions in three fresh-process repeats. Final nonincremental
Release build: zero warnings/errors. Local regression: 4,771 passes in 81 reports.
Discovery: 4,981 including all 54 additions; stable qualification inputs: 1,484.
See artifacts/core-refactoring/error-forward-tracing/verification.json and
docs/reviews/2026-09-11-error-forward-tracing.md. Initial failed reports are preserved.

FlowTransformNode's separate native forwarding stage remains outside its transform
operation, not silently covered by this evidence. Delivery-stage/source/transport/
durable/revision and non-message command contexts, callback discovery/latency, enabled
runtime measurements/retention and refreshed local packages remain open. Prior
performance/package evidence predates the current Core. No publishing or coordination.

## Latest checkpoint: coalesced metric snapshot context (2026-09-11)

The final snapshot now reuses Core processing instrumentation instead of inheriting
completion-worker context. It is a root with one bounded correlation link to the last
accepted input, not all-history provenance. Existing sample updates keep their normal
wrapper scopes. The original typed accepted input is stored only for coalesced mode
and cleared after flush/disposal; final correlation uses one null-payload canonical
envelope, without retaining unused original payload fields or adding a context registry.

Fifty new cases across both frameworks qualify sampling, invalid carriers/rejected
tail, one/64-input histories, no unnecessary final operation, listener failures and
bounded-output terminal outcomes. All 102 Metrics tests pass; final repeats add 150
passing tracing executions. Source API row 37 changes 89 -> 90 for an override of
the existing disposal hook inside a sealed class, not a new consumer API. The
reviewed baseline and both API tests are recorded; first-run failure logs are kept.

Final fresh nonincremental Release build: zero warnings/errors. Selected local tests:
4,717 passes in 81 reports. Discovery: 4,927; stable inputs: 1,484. The increase of 76
includes the 50 additions and 26 existing Metrics tests now also exercised on net8.
Evidence: artifacts/core-refactoring/metric-final-tracing/final/verification.json and
docs/reviews/2026-09-11-metric-final-tracing.md.

This closes the specific coalesced history-correlation policy gap. It does not bound
existing rate-window queues or qualify every aggregate component. Remaining source/
transport/durable/revision and non-message error contexts, callback discovery/latency,
enabled runtime load/retention and refreshed local packages remain open. Historical
performance/package evidence predates current changes. No publishing or coordination.

## Latest checkpoint: join blocked-output terminal tracing (2026-09-11)

The new deterministic tests exposed a completion-flush bug: forced shutdown ignored
the existing stopping token during bounded output admission. The one-line fix passes
that token; graceful completion still drains, forced fault cancels the held operation,
and the original owner failure remains authoritative. No new API, state or runtime.

Six match/timeout x drain/fault/decline cases run on both frameworks (12 additions).
They prove spans end at admission rather than downstream consumption, exact ordered
drain/lineage, cancellation versus admission failure, original fault identity and
scope cleanup. Wider Join tests pass 130 cases; the final narrow tests pass 12 plus
36 executions in three fresh-process repetitions.

Fresh nonincremental Release solution build: zero warnings/errors. Selected local
regression: 4,641 passes in 80 reports. Discovery: 4,851 including all 12 additions.
Stable qualification inputs: 1,483. Evidence:
artifacts/core-refactoring/join-terminal-tracing/verification.json and
docs/reviews/2026-09-11-join-terminal-tracing.md.

This closes the specified held-output match/timeout terminal trace gap, not all
aggregation, non-message error or callback behavior. Bounded history, remaining
source/transport/durable/revision context, enabled load/retention and fresh local
packages remain open. Historical measurements/packages predate the current changes.
No publishing or external coordination.

## Latest checkpoint: join operation context (2026-09-11)

Join input commands, matched-result creation/output admission and timeout emission
now reuse Core processing scopes. Matches link both input contexts without choosing
one causal parent; result business lineage stays left-sided. Non-message commands
isolate worker ambient context. The existing helper has a small public extension
surface for custom components, not another runtime or friend-assembly dependency.
Only Core's source API row changes (419 -> 427), documented as an additive API in
the existing unpublished preview train.

New tracing cases: 76 across net8/net10. Final focused join suite: 118 passes. Routing
tests now cover both frameworks, adding 54 existing tests on net8. Full clean Release
build: zero warnings/errors. Selected local regression: 4,629 passes, zero failed/
skipped, 80 reports. Discovery: 4,839; stable inputs: 1,483. See
artifacts/core-refactoring/join-tracing/verification.json,
docs/reviews/2026-09-11-join-operation-tracing.md and .testagent/join-tracing-review.md.

Next qualify trace outcomes while join output is blocked and canceled/faulted; then
bounded history aggregation and remaining source/transport/durable/revision scopes.
Discovery/slow callbacks, enabled telemetry/load/retention and refreshed local
packages remain open. No publishing or external coordination.

## Latest checkpoint: concurrent fan-in context (2026-09-11)

Ordinary independent-message fan-in now has deterministic concurrent evidence for
both processing wrappers and off/None/propagation/full sampling, with shared and
independent traces. No production abstraction or code change was needed. The new
32 cases prove per-input parent/link, independent spans across awaits, exact
ownership, disabled header inheritance, derived carrier and drain.

Full nonincremental Release build: zero warnings/errors. Selected local regression:
4,499 passed, zero failed/skipped, 79 reports. Discovery: 4,709 including all 32 new
cases. Stable qualification inputs: 1,482. Evidence:
artifacts/core-refactoring/fanin-tracing/verification.json and
docs/reviews/2026-09-11-concurrent-fanin-tracing.md.

Next concrete production correction: JoinNode command context and both-input links
at matched-result creation. Its source currently lacks that operation boundary;
ordinary fan-in proof must not close aggregation acceptance. Incremental metric
history is a separate bounded-policy review. Existing source/transport/durable/
revision/callback/load/performance/final-package gates remain active. No publishing.

## Latest checkpoint: shared request tracing (2026-09-11)

The existing Core exchange now owns one optional request activity from accepted
registration through admission cleanup. It reuses the tracing helper and immutable
exchange envelope; no coordinator, public API or host telemetry dependency is added.
Caller/fallback context, sampling, terminal outcomes, callback isolation and distinct
concurrent exchanges are qualified by 46 new cases across both frameworks.

Fresh nonincremental Release build: zero warnings/errors. Selected local regression:
4,467 passed, zero failed/skipped in 79 reports. Discovery: 4,677; stable inputs:
1,482. Evidence: artifacts/core-refactoring/request-tracing/verification.json and
docs/reviews/2026-09-11-request-operation-tracing.md. Detailed R1-R7 mappings are in
.testagent/request-tracing-review.md.

Source/fan-in/transport/durable/revision coverage, discovery/slow-callback behavior,
enabled telemetry/load/retention and fresh package qualification remain open. The
following diagnostic measurements predate this Core change; they do not establish
current-tree performance acceptance. No publishing or external coordination.

## Latest measurement: diagnostic workflow effect (2026-09-11)

All 48 runs completed successfully (16 smoke, 32 timed; session 66107). Only Engine
DLL/PDB differs between matched variants; current benchmark/Core/dependencies are
shared. All delivery/drain checks pass; 1481 source inputs and 244 runtime files
remain stable. Every timed pair improves allocation and throughput. Paired median
throughput gains are 13.17%/15.02% for small net8/net10 graphs and 26.49%/24.22% for
deep graphs. Allocation medians fall 14–18%, roughly 3.5 KB/small completion and
96–101 KB/deep completion. These are listener-free, workload-specific measurements,
not full performance acceptance or a retained-memory improvement claim.

Evidence: artifacts/core-refactoring/typed-diagnostics/load/results and
docs/reviews/2026-09-11-diagnostic-workflow-measurement.md. Keep this simplification;
do not repeat the same comparison without new evidence. Next qualify the actual
request-operation boundary at the shared Core exchange rather than synthetic
diagnostic receipt. Broader telemetry/capability/load/final-candidate gates remain
open. No further production changes, publishing or external coordination occurred.

## Latest checkpoint: typed internal diagnostic publication (2026-09-11)

Activity/request/internal delivery-failure producers no longer encode and then
immediately decode their diagnostic. An internal typed overload creates one
canonical message and snapshots normalized instrumentation data; the existing
message overload still validates by decoding. Shared observer/admission behavior,
bounded Dataflow streams and canonical message identity remain unchanged.

50 focused tests and all 4421 local regression cases pass; non-incremental full
Release build has zero warnings/errors. Discovery is 4631 (+12), with 1481 stable
inputs. Original line-count aggregation failed on concatenated discovery output;
raw-name prefix validation resolves it without rerunning or hiding any test failure.
Eight allocation-probe processes show lower allocation in all sixteen scenario
pairs, roughly 1.0–1.2 KB per stopped diagnostic publication. This isolates encoding
preparation, not whole-workflow performance. See
docs/reviews/2026-09-11-typed-diagnostic-publication.md and
artifacts/core-refactoring/typed-diagnostics/verification.json.

Next qualify matched workflow effects and continue the outstanding telemetry,
capability and final-candidate gates. The earlier local package feed is historical;
no publishing or external consumer coordination has occurred.

## Latest measurement: event capacity workflow effect (2026-09-11)

Completed 16 smoke and 32 timed workflow runs (session 42040, exit zero). Each
before/after pair differs only in Core DLL/PDB; Engine/benchmark/other dependencies
are identical. All delivery/drain checks pass, with 1,481 source inputs and 244
runtime files unchanged. Direct workflows have smoke coverage; timed runs cover
small/deep revision-enabled paths, four alternating pairs per framework.

Allocation falls in all 16 timed pairs: paired medians about 1.7 KB/completion for
small graphs and 42 KB for deep graphs. Deep throughput medians improve 4.37% net8
and 5.14% net10, with timing variation and a large small-net10 outlier disclosed.
This is a workload-specific benefit, not full performance acceptance. No further
production changes. Evidence: event-capacity/load/results. Review:
docs/reviews/2026-09-11-event-capacity-workflow-measurement.md. Next inspect the
remaining diagnostic encode/decode duplication with its validation and observer
ordering preserved; broader telemetry/capability/final-package gates remain open.

## Previous checkpoint: event allocation attribution and correction (2026-09-11)

Offline allocation analysis reconciles raw and converted sample/type/byte totals.
Diagnostic ReportActivity stacks account for about 73% of sampled allocation weight
in the deep update workload; ToMessage and dictionary resize stacks identify a
concrete avoidable cost. Pre-sized four existing dictionary allocations in
FlowEvent.cs, retaining all event conversions, schema, validation and behavior.

Three paired repeats per framework show about 552 bytes saved per encoding (15.8%)
on net8 and net10, with identical non-target probe binaries. Fresh nonincremental
full build: zero warnings/errors; local regression: 4,409 passed, 79 reports;
discovery: 4,619. Source inventory stable at 1,481 entries, only FlowEvent.cs changed
since the before measurement. No new tests or assertions removed. Initial artifact
TRX aggregation needed literal paths for bracketed filenames; validation corrected
without rerunning successful test commands. All relevant processes are terminal.

Review: docs/reviews/2026-09-11-event-allocation-analysis.md. Evidence under
host-allocation-trace/analysis/verified-results and event-capacity. This proves the
specified encoding allocation improvement, not a whole-workflow speedup. Wider
performance/telemetry/capability gates remain open. The frozen local feed predates
this correction and must be refreshed at final qualification. No publication.

## Previous diagnostic checkpoint: current-runtime allocation traces (2026-09-11)

Collected direct and revision-enabled deep-graph allocation/GC traces from the current
Release benchmark runtime. All seven loaded library packages match the qualified
candidate inventory; 1,481 source inputs and 59 runtime files remained unchanged.
Both synthetic workloads exit zero and pass exact delivery, admission, bounded
tracking and business-drain checks. Raw traces total 284,814,892 bytes.

The validator initially failed to identify allocation events because merely reading
the CLR parser did not register the required event templates. Explicit typed event
subscriptions resolve the check without changing/recollecting traces. Validated:
81,707 direct and 117,168 update allocation ticks, one process each, zero event loss,
matching Core/Engine symbols. Collection session 96944 and validation session 55607
are terminal, exit zero. Evidence: artifacts/core-refactoring/host-allocation-trace.

This is collection and integrity validation, not allocation attribution or a speed
measurement. Next analyze type/call-stack allocation weights and correlate current
source. Preserve startup/warmup/sampling caveats; do not substitute sampled thread
time for allocated bytes. The trace-collection workflow separates this checkpoint
from offline analysis. Full performance and overall goal remain open.

## Previous measurement: bookkeeping comparison complete (2026-09-11)

The previous goal turn completed the fresh local package checkpoint. Current work
measures the removal of unused ordinary dispatch bookkeeping against the frozen
notification-optimized baseline. Only the qualified Core/Engine binary pair differs;
benchmark and other dependencies stay identical. All 16 smoke and 64 timed runs
pass; session 94278 ended with exit code zero. The run verifies 1,481 stable source
inputs and 248 stable runtime files. Timing is mixed: direct-deep throughput median
differences are -1.35%/+0.13% (net8/net10); update-deep differences -2.31%/-3.64%,
with net10 slower in all four pairs. No general speedup or performance acceptance.
Review: docs/reviews/2026-09-11-bookkeeping-performance.md. Do not restart the terminal
session or overwrite frozen evidence. Next investigate actual optional-host allocation
and diagnostic cost, preserving its qualified routing/queue ownership semantics.

## Latest completed checkpoint: coherent local package candidate (2026-09-11)

Created all 81 manifest packages and 81 symbol archives with the unified local-only
version 0.0.0-local.20260911.t0930. Both supported frameworks and every internal
dependency coordinate are verified. Fresh package-only consumers restore all 81
from isolated caches on net8 and net10, hash-match the archives and pass the existing
workflow/resource/health/fluent/storage and separate-process durable restart checks.

The first consumer exposed missing terminal output intent in the acceptance fixture.
Migrated only its two C# files to explicit DiscardUnmatched; all assertions and strict
upstream routing remain. No production change. The failed attempts are preserved.
Fresh full nonincremental build has zero warnings/errors, all 252 release checks
pass and 4,619 cases are discovered. All 162 rebuilt package DLLs match their frozen
archive hashes; 1,481 source inputs stay stable during successful consumption.
Evidence: artifacts/core-refactoring/local-candidate-20260911-0930/final-verification.json.
Review: docs/reviews/2026-09-11-local-package-candidate.md.

This closes the fresh local archive/representative-consumer checkpoint, not complete
performance, telemetry or provider/integration qualification. Full goal remains active.
Nothing published; external consumer has not tested this candidate.

## Previous checkpoint: timer verification hang resolved (2026-09-11)

Heap analysis identified a timer-test scheduling race, not a production deadlock.
The test advanced virtual time after only its first timer registration; the remaining
message was due at 80 ms while the clock stopped at 40 ms. Its receive timed out,
then graceful disposal waited forever. The exact verified testhost was terminated
after evidence collection; the old regression ended failed and remains preserved.

The existing burst test now waits for native input processing before advancing the
clock and faults the node on failed test cleanup. It retains all exact burst ordering
assertions and adds preadvance completion/final timer checks. No production changes.
The focused test, 20 repeat processes, timer suite and full nonincremental Release
build pass. Fresh verification completes with 4,409 local regression passes,
4,619 discovered cases (including all four native prepared-output cases), and 1,481
stable source inputs. All 24 logged commands exit zero. Evidence is under
artifacts/core-refactoring/timer-burst-verification. See
docs/reviews/2026-09-11-timer-burst-verification.md. The full goal remains active.

## Previous checkpoint: revision queue ownership audit (2026-09-11)

Inspected candidate staging, stable output and stable input against their actual
policies. Delaying native source attachment is not a valid staging replacement:
canonical output discards unlinked messages. Stable output retains capture/selection
ordering; stable input retains claimed-versus-queued generation handoff. These are
distinct optional boundaries using native bounded blocks, not a second fanout batch
implementation. Do not repeatedly reopen their existence as an unexplained finding.

Four new cases use real canonical output at capacities 1 and 3 to verify retention,
ordered reference identity, drain and abandoned-candidate/replacement isolation.
Focused tests and a fresh full nonincremental Release build pass. Broader regression
is incomplete: the timer suite remains live without a final report; solution discovery
has not started. Existing session 99756 is still running verify.ps1. PID 119324 was
verified as its timer testhost through parent 38336. Managed stacks do not support
the initial lock-deadlock hypothesis; async state needs separate inspection. Stack
evidence is timer-stacks-local.txt; an earlier cross-identity diagnostic attempt was
denied and preserved in timer-stacks.txt. Do not restart or mark this run passed.
Evidence remains in artifacts/core-refactoring/revision-queue-audit.
See docs/reviews/2026-09-11-revision-queue-ownership.md for exact contracts and limits.
No production code changed in this checkpoint. Overall semantic convergence, fresh
performance qualification, the full feature ledger and local packages remain open.

## Previous checkpoint: offline dispatch analysis and scoped bookkeeping (2026-09-11)

Validated the four sampled-stack exports against independent method reports and
preserved trace identities. Direct lock-method sampled time is nearly constant while
pool waiting changes substantially; percentage changes alone do not explain the
slowdown. Instrumented timings are not fresh benchmark scores or causal evidence.

Removed two unused volatile writes from ordinary output delivery. Prepared delivery
retains its active count under an explicitly scoped internal name; the hosted getter
and existing test references were updated. All 731 Core/Engine cases pass and the
full nonincremental Release build has zero warnings/errors. Fresh
paired timing and broader qualification of this source revision remain pending.

The physical queue audit identifies staging, stable output and stable input stages
in revision-enabled delivery. Their necessity must be proved against activation,
generation, ordering, capture and drain contracts; shared helper classes do not close
that audit. See docs/reviews/2026-09-11-dispatch-analysis.md. The full goal and local
package candidate remain open; nothing published.

## Previous checkpoint: dispatch diagnostic capture and scope check (2026-09-11)

Collected four process-local managed traces on net10 from the frozen direct-migration
and hosted-notification before/after runtime pairs. Each traced child passes delivery,
tracking and business-drain checks. Native trace parsing and stack export succeed;
all traces report zero event loss. Binary identities, process IDs, UTC session times,
exact commands and file hashes are recorded. All 1,480 source inputs remain unchanged.
No production source or tests changed, and no unrelated process was traced.

These instrumented runs are diagnostic artifacts, not fresh performance scores or a
causal explanation. Offline dispatch/runtime-event analysis remains next; performance
sign-off is still open. Evidence: artifacts/core-refactoring/dispatch-trace/README.md
and results/verification.json, trace-metadata.json and collections.json.

Rechecked convergence scope against the approved plan: separate ordinary/revision
startup and drain policy is explicit; a second transport/fanout/exchange executor is
forbidden. Identical orchestration class names or an unused FlowApplication wrapper
are not acceptance requirements. The next audit must trace actual queues and owners,
not infer completion from shared helpers or infer duplication from class names alone.
See docs/reviews/2026-09-11-runtime-convergence-acceptance-scope.md. No convergence
gate is closed by this clarification. Wider qualification, local packages and the
full goal audit remain open. Nothing published.

## Previous checkpoint: hosted notification allocation (2026-09-11)

ApplicationOutputPort now reuses one cancellation-check callback for empty receive/
observation snapshots, retaining per-message closures only when observers exist.
This is the sole production change; Core, routes, public APIs and dependencies are
unchanged. A synchronous attribution probe confirms exactly 112 fewer bytes per empty
preparation on both frameworks, with unchanged receive-present allocation/identity.

Fresh qualification passes 45 focused cases, a clean full Release build, 4,405 local
regression cases and one local workflow acceptance. Eight smoke plus 32 alternating
timed hosted workloads pass, with 1,480 stable source inputs and 248 stable runtime
files. Whole-workload allocation decreases in 15/16 pairs. Small-host medians save
about 452 bytes/completed; deep-host medians save about 8.9–10.3 KB/completed.

Performance is still NOT signed off. Small-host throughput is lower in 7/8 pairs
(median -4.64%/-3.88%), while deep-host throughput is higher in 8/8. All results,
including unfavorable allocation and latency cases, are retained. Profile actual
dispatch/scheduling before explaining the timing differences; the earlier direct-path
finding remains separate because Core was not modified. Evidence and limits:
.testagent/notification-allocation-review.md and artifacts/core-refactoring/notification-paired.
The first allocation probe setup failed due to no active source; its evidence is
preserved and the corrected setup retains the identity assertion.

Full graph/lifetime convergence, wider qualification, fresh packages and the final
goal audit remain open. Nothing published.

## Previous checkpoint: repeated dispatch comparison (2026-09-11)

No production source or tests changed in this checkpoint. Isolated historical/current
Engine builds use identical settings, verified historical source hashes and matched
supporting dependencies. Preserved historical Core binaries are verified against
their symbol source hashes and DLL/symbol identities. All 16 smoke and 64 timed
workloads pass; 1,480 source inputs and 244 frozen runtime files remain unchanged.

Four alternating pairs per case confirm a small-host allocation increase in all eight
framework pairs: approximately 257–637 bytes/completed, medians +1.36%/+1.44%.
Direct deep graphs are slower in seven of eight pairs, with median throughput changes
-3.54% on net8 and -2.30% on net10. Hosted deep throughput differs by framework.
Performance remains unaccepted; these descriptive results are not a significance
test or full workload qualification. The shared Core executor stays in place.

Next: investigate the new per-message notification closure without weakening
cancellation/observer snapshots, and separately inspect direct-path dispatch overhead.
Evidence and exact scope: .testagent/dispatch-paired-review.md and
artifacts/core-refactoring/dispatch-paired/results/verification.json and comparison.json.
Full graph/lifetime convergence, wider qualification, fresh packages and final audit
remain open. Nothing published.

## Previous checkpoint: canonical output executor migration (2026-09-11)

The optional host's output ActionBlock, dispatch loop and fanout batch call are removed.
ApplicationOutputPort now uses the actual Core FlowOutput queue and delivery executor.
An internal route-admission contract is shared by native fanout links and existing
hosted revision routes. The host prepares routes after capture under its revision
gate, then releases the gate before Core starts and joins target admissions. Observation
remains post-admission; Core has no capture, revision or observation types. No extra
queue, public API or package dependency was added. Native ordinary dispatch keeps its
direct task entry, avoiding an extra asynchronous wrapper there.

Three new deterministic tests (six framework cases) and 118 focused hosted cases pass.
An initial missing test-friend declaration caused 23 TypeLoadException failures and was
fixed. The first full regression had one source-declaration baseline mismatch from two
members of the private FanoutLink class; only the reviewed Core baseline row changed.
Earlier failures remain preserved. Fresh final qualification passes all 149 commands:
clean full build, 4,405 regression cases, 4,615 discovered, one local acceptance and
all 144 count workloads. All 1,483 captured inputs stayed unchanged. Sixteen matched
timed cases also pass delivery/drain checks with stable inputs, but performance is
NOT accepted: 14/16 single-run throughputs are lower and small update-host cases
allocate approximately 240–632 additional bytes per completed message (0.9–2.5%).
Repeat controlled before/after measurements to resolve the regression risk before
performance sign-off. Do not infer a speedup or no regression from these diagnostics.

This is actual output-executor convergence, not full graph/lifetime convergence. The
host still constructs route snapshots and orchestrates stable-input/candidate lifetimes
outside FlowApplication/FlowExecution. That remaining architecture, broader telemetry/
evaluator/exchange/revision qualification, fresh coherent local package candidate and
final audit remain open. Evidence: .testagent/dispatch-convergence-review.md and
artifacts/core-refactoring/dispatch-convergence-final-verification.json,
dispatch-convergence-timed-verification.json and dispatch-convergence-comparison.json.
Nothing published.

## Previous checkpoint: direct event scalar encoding (2026-09-11)

Removed redundant string/null/canonical-value materialization and empty temporary
maps from event serialization. FlowEvent.cs is the only changed production file in
this checkpoint; authoring identity, snapshots, validation and event delivery remain
unchanged. No new abstraction, queue, public API or dependency was added.

Five added test methods expand to 22 cases across both frameworks. Focused selection
passes 34 cases before and after. The unchanged synchronous allocation probe measures
approximately 4,016 to 3,488 bytes per event encoding: 528 bytes fewer, about 13.1%.

Fresh qualification: clean nonincremental Release build, 4,399 regression passes,
4,609 discovered cases, one local acceptance, 144 count and sixteen timed workloads.
Both source snapshots preserve 1,481 inputs. Matched configuration/payload/environment
and unchanged benchmark sources are verified. Update-host allocation per completed
message is 6.3–8.9% lower in these single-run comparisons; timing is not a repeatable
speedup claim. Ordinary-path allocation varies both ways and is not attributed to
the encoder. Observation quiescence and leak freedom are not established by drain.

Evidence: .testagent/event-scalar-review.md and event-scalar-verification.json,
event-scalar-timed-verification.json, event-scalar-comparison.json under
artifacts/core-refactoring. The central next architectural task remains removing
the independent update-host business-route dispatcher while preserving capture,
revision and stable-input contracts. Shared helpers and this allocation improvement
do not satisfy canonical graph convergence. Wider qualification, the fresh coherent
local package candidate and final audit remain open. Nothing was published.

## Previous checkpoint: canonical payload/topology qualification (2026-09-11)

Extended the existing actual-runtime load harness with deterministic binary content
inside FlowValue, preserving JSON defaults. Actual shared payload fingerprints are
computed before measurement and checked across framework/runtime/topology cases.
No production src/ file, component API, queue or project dependency changed.

The first matrix/timed runs passed, but test review found requested-kind metadata did
not prove actual payload identity. Eight strengthened cases failed on the missing hash,
then passed after the setup-only fingerprint was added. Final focused selection: 46
cases, eleven more than before. One checker quoting failure occurred before execution;
the draft and earlier evidence remain preserved. Final corrected scripts are verified.

Fresh identity-qualified evidence: clean full nonincremental Release build, 4,377 local
regression passes, 4,587 discovered cases, one separate local workflow acceptance,
144 passing count workloads and 16 representative timed workloads. The count matrix
covers 1/8/32 hops, 1/2/8 targets, 256/65536 bytes, JSON/binary, ordinary/update and both
frameworks. Both final snapshots retain 1,480 unchanged inputs. Timed cases remain
single-run diagnostics, not final performance budgets or the full repeated matrix.

The wide/deep update path remains costly: roughly 650–668 KB allocated per completed
envelope versus 8.5–9 KB ordinary in the representative runs, including harness costs.
Capture/selection/snapshot constraints are recorded in the convergence audit; another
unused Core application wrapper would not close that requirement. An apparent extra
JSON clone was investigated and ruled out: owned clones/export allocate zero on both
installed frameworks. Preserve the ownership boundary and investigate measured costs.

Evidence: .testagent/payload-matrix-review.md, payload-identity-verification.json,
payload-identity-timed-verification.json and payload-topology-findings.md under
artifacts/core-refactoring. The broader runtime/telemetry/evaluator/exchange/revision
requirements, fresh package-only candidate and final goal audit remain open. Nothing
was published or coordinated with an external consumer.

## Previous checkpoint: shared dependency planning (2026-09-11)

Core topology validation/freeze and optional staged drain now share one internal
FlowDependencyOrder algorithm. Frozen Core graph layers also supply entry components,
removing a separate root search. Duplicate edges, stable caller order and cycle failure
semantics are retained. The optional host preserves its own ordinal ordering and
self/outside/control-link exclusions; revision barriers and native component execution
are unchanged. No public API, project dependency, scheduler or creator was added.

Verification: 138 focused Core cases across both frameworks and 77 Engine hosting/
assembler/control cases pass. Fresh qualification has ten successful commands, clean
nonincremental Release build, 4,366 local regression passes, 4,576 discovered cases,
one separate local workflow acceptance and six exact-count/slow-target load reports.
All 1,478 captured inputs stayed unchanged. Evidence: .testagent/dependency-order-review.md
and artifacts/core-refactoring/dependency-order-verification.json.

The shared planner is a concrete simplification, not complete optional graph/runtime
convergence. No new throughput claim. The full telemetry/workload/exchange/revision
matrix, fresh isolated package-only candidate and final approved-goal audit remain
open. Nothing was published or coordinated with an external consumer.

## Previous checkpoint: immutable event context enrichment (2026-09-11)

ApplicationSignalMessage now enriches existing immutable headers through a native
builder. ApplicationEventStream uses immutable SetItem and forwards the original
envelope when context does not change. Exactly these two internal production files
changed from the load baseline; no Core, public API, queue, event suppression, registry,
new abstraction or dependency. Canonical metadata, value/error identity, caller payload
snapshot and native event-stream behavior are retained.

Four new reuse cases failed before the change; all eight new cases and 91 selected
event/diagnostic cases pass afterward. The isolated context operation allocated 592
bytes versus 1,336 for full copying. In repeated matched fast workloads, update-host
whole-workload allocation per completed envelope fell roughly 17–19% on both runtimes.
Timing varied, including unchanged ordinary controls, so no general throughput gain
is claimed. Application events are not disabled just because there is no telemetry SDK.

Fresh qualification: 18 successful commands; zero-warning/error full nonincremental
Release build; 4,350 local regression passes; 4,560 discovered cases; one separate
local workflow acceptance; 14 load reports (four CLI smokes, eight fast measurements,
two slow .NET 10 measurements). All 1,476 source/build/test inputs remained unchanged.
Comparison verifies matching workload/runtime/machine configuration and exactly the two
intended production changes. Evidence: .testagent/event-context-review.md,
artifacts/core-refactoring/event-context-verification.json and event-context-comparison.json.

This is verified simplification, not final completion. Significant optional-host overhead,
actual Core graph convergence, event encode/decode work, the complete telemetry/request/
revision/payload/topology matrix, fresh isolated package-only acceptance and final audit
remain open. Nothing was published or coordinated with an external consumer.

## Previous checkpoint: matched bounded-load baseline (2026-09-11)

The new load command is confined to the existing benchmark executable; production
runtime code, native queues and public API are unchanged. It runs identical component
instances/topologies through ordinary Core and actual update hosting, tracks original
envelopes before admission, verifies exact reliable fanout, and records real per-message
latency, bounded backlog and managed/process memory. Microbenchmark jobs are unchanged.

The initial update-host run exposed a harness capacity-model error: prepared-output
staging buffers were missing from the bound. Source inspection and three additional
test cases corrected that model, not production backpressure. Initial failed evidence
is preserved. Corrected qualification completed all 26 commands, including 22 load
reports, 35 focused cases, a zero-warning/error full nonincremental build, 4,342 local
regression tests and one local acceptance. Discovery: 4,552. All 1,475 captured inputs
unchanged. Five regression reports are intentionally empty under explicit prerequisites.

Both runtimes retained exact delivery and drained to zero business work. The repeated
fast workloads show a large update-host overhead (see exact distributions/allocation
semantics in artifacts/core-refactoring/load-baseline-review.md). Slow sinks also expose
the additional queueing latency of optional hosting. This evidence changes the next
action: attribute and simplify the eager event/diagnostic and stable-port path while
preserving events, dynamic observation and revision guarantees. No arbitrary performance
budget or leak-freedom claim follows from these baseline samples.

Authoritative reports: artifacts/core-refactoring/load-corrected-verification.json and
.testagent/load-qualification-review.md. Actual Core/update graph convergence, the full
condition/payload/telemetry/request/revision matrix, fresh isolated package-only consumer
and restart qualification, and final feature audit remain open. Nothing was published
or sent to an external consumer task. The full approved goal remains active.

## Previous checkpoint: queued source control tracing (2026-09-10)

Source controls now reuse the existing Core helper for actual awaited control actions,
restoring the incoming message context and exposing flow.control with a local port
tag. The original native queues, instance-first construction, exact message identity,
owner fault, cancellation and graceful drain stay unchanged. No public API, dependency,
extra queue, creator, telemetry runtime or source-production lifetime span.

Six reviewed methods cover 32 new cases across both supported frameworks. Sixteen
pre-change failures reproduced the missing boundary. Full nonincremental Release
build: 34.71 seconds, zero warnings/errors. Local regression: 4,307 passed, zero
failed/not-executed, 79 Completed reports (five intentionally filtered empty under
external/container prerequisites), including 444 Foundation, 435 Engine and 252 release
cases. Source declaration baseline unchanged. Discovery: 4,517; separate local
acceptance: one passed; five both-framework repeats: 160 additional passed executions.
All 1,465 captured source inputs remain unchanged.

The original qualification wrapper finished every command successfully, then failed
its own namespace-count assertion by excluding data-contract tests from the Foundation
assembly. The separate assembly-based verifier passes against the preserved reports
and original hashes. No result or snapshot was rewritten to bypass that failure.
Exact review: .testagent/control-trace-review.md; final machine evidence:
artifacts/core-refactoring/control-trace-verification.json. All commands are terminal.

The approved full goal remains active. Other operation/transport telemetry, actual
hosted graph convergence, matched overhead/load/retained-memory measurements, fresh
isolated package/consumer restart acceptance and final handoff remain open. Historical
performance/package results are not current proof. Nothing was published or sent to
an external consumer task.

## Previous checkpoint: executable condition revision boundary (2026-09-10)

The runtime-convergence review found a concrete route-only update defect: compiled
conditions could change while their text stayed the same, leaving affected endpoint
sets empty. Activation could then skip the output gate while prior work was held in
capture. Full component replacement can mask this through its output attachments.

The existing internal route identity now includes its conditional compiled-link
reference. Preparation conservatively treats recompilation as change without guessing
semantic equality of native code/parameters. The same compiled instance and equivalent
unconditional links stay unchanged. Duplicate-declaration validation intentionally
excludes executable identity. The existing transaction, capture/selection gates and
canonical delivery kernel perform coordination; no new runtime/queue/interface,
public API, dependency or Core production change. README describes the policy.

Five cases reproduced the old defect. Ten focused cases now pass, including held
capture, native configuration, portable parameters, canceled activation recovery and
duplicate validation. Five repeat runs add 50 passing executions. Full nonincremental
Release build: 38.05 seconds, zero warnings/errors, both production frameworks.
Selected regression: 4,275 passed, zero failed/not-executed; 79 Completed reports,
five intentionally empty under explicit external/container prerequisite exclusions.
Includes all 435 Engine cases and 252 release checks; public source baseline unchanged.
Separate in-process acceptance: one passed. Discovery: 4,485. All 1,462 captured inputs
remain unchanged. Exact acceptance/assertion review: .testagent/revision-condition-review.md;
machine-readable evidence: artifacts/core-refactoring/revision-condition-verification.json.
Every command is terminal with exit zero. No publishing or external coordination.

This fixes one transaction contract, not the entire approved architecture. Other
runtime-convergence evidence, operation telemetry, matched overhead/load/retained-memory
measurements, fresh isolated packages, actual package-only consumer/restart acceptance
and final handoff remain open. Previous performance/package results are historical.

## Previous checkpoint: diagnostic observer boundary (2026-09-10)

Removed synthetic activities on diagnostic receipt. Actual Core processing spans
remain, and logs/source callbacks retain their caller or processing context. Counter,
duration, logger and diagnostic-source recording have separate failure boundaries.
Logger filtering and independent metric enablement are respected; invalid/canceled
publication still has no observer side effects. One validated event is reused instead
of decoded twice, with a redundant validation wrapper removed. No new runtime, queue,
component creator, Core dependency, SDK/exporter or subscription system.

ActivitySourceName is intentionally removed; hosts select the Core source for actual
processing. The Engine declaration baseline changes only the reviewed manifest row
from 388 to 387. Migration is documented in the hosting guide, Engine README and
package notes. Existing breaking candidate versions are not republished; fresh unique
local candidate versions still belong to the package gate.

38 new cases pass inside 107 focused checks. Full nonincremental Release build:
36.10 seconds, zero warnings/errors, both production frameworks. Selected local
regression: 4,265 passed, zero failed/not-executed; 79 Completed TRX reports, five
intentionally empty under explicit external/container prerequisite exclusions.
Includes all 425 Engine cases, 252 release checks and 80 Core tracing cases. Separate
in-process acceptance: one passed. Serial discovery: 4,475. All 1,459 captured inputs
remain unchanged. Exact checklist and assertion/gap review:
.testagent/diagnostic-observers-review.md; machine-verifiable evidence:
artifacts/core-refactoring/diagnostic-observers-verification.json.

The four qualification commands succeeded. The initial PowerShell 5.1 wrapper failed
in its final command-list JSON parsing; the unchanged verifier succeeds in PowerShell 7
against the original hashes and reports. Red fixture/API-review evidence is preserved.
All commands are terminal. No publishing or external consumer coordination.

This completes the diagnostic-observer slice, not the approved goal. Host callbacks
remain synchronous; discovery/current-change callback behavior needs qualification.
Other control/custom/source/fan-in/request/transport/durable/revision boundaries,
hosted graph convergence, overhead/load/retained-memory measurements, fresh isolated
packages, package-only consumer/restart acceptance and final handoff remain open.
Prior performance and package snapshots are historical, not current-tree proof.

## Previous checkpoint: queued processing trace context (2026-09-10)

Core action/transform wrappers now emit activities around actual awaited processing.
New messages capture W3C creation context in existing immutable headers; ordinary
forwarding, conversions and persistence preserve it. Fanout keeps the same envelope
reference and distinct sibling spans; derived messages capture their processing
context. An unrelated background activity cannot overwrite message context, even
when tracing is disabled. SDK/exporter and baggage policy remain host-owned.
No creator, public construction API, message field, registry or second runtime.
Contract and explicit limits: docs/processing-trace-context.md.

40 new tracing cases pass per framework inside 206 Foundation cases each. Full
nonincremental Release build: 23.13 seconds, zero warnings/errors. Selected regression:
4,227 passed, zero failed/not-executed, 79 Completed reports (five intentionally empty
under external/container prerequisite exclusions), including all 252 release checks.
Separate local in-process workflow acceptance: one passed. Serial discovery: 4,437
cases. All 1,453 captured inputs stayed unchanged. Exact requirement mapping and
red/green review: .testagent/processing-trace-review.md; reproducible verification:
artifacts/core-refactoring/processing-trace-verification.json.

The first broad run caught a source-declaration API guard mismatch on the internal
scope's Dispose. Explicit interface implementation resolved it with the original
baseline unchanged. Failed evidence is retained; final qualification is fresh.
All commands are terminal. No publishing or external consumer coordination.

The full goal is not complete. Remaining telemetry includes custom/control/source,
fan-in, request/reply, transports and durability/revision boundaries; synthetic host
diagnostic activities and provider behavior still need correction/qualification.
Overhead, sustained load and retained-state recovery, a fresh isolated 81-package
candidate, package-only consumers and final handoff remain open. Previous performance
and package snapshots are historical after this Core change, not fresh proof.

## Previous checkpoint: bounded runtime metrics (2026-09-10)

Diagnostic metric labels now use only canonical kind/level with finite Unknown
fallbacks (at most 25 combinations). Per-event names, subjects, caller dimensions
and identities no longer define metric series. The arbitrary-value histogram is
replaced by a fixed-ms request-duration histogram; all custom measurement/event data
remains intact for consumer-owned reporting. The metric-schema migration and its
accepted-diagnostic loss semantics are documented in docs/05-hosting-and-observability.md.

One production file changed, with eight fewer lines and no new Core API, abstraction,
creator, SDK/exporter or package dependency. 35 new cases cover exact labels, undefined
enums, valid/invalid durations, raw event identity, a held real request and throwing
counter/histogram listeners. All 387 Engine cases and 252 release checks pass.
The full nonincremental Release solution build passed in 36.66 seconds with zero
warnings/errors; all 1,391 captured inputs remained stable. See
.testagent/runtime-metrics-review.md and artifacts/core-refactoring/runtime-metrics-verification.json.

Operation tracing/context propagation, independent/slow synchronous-provider behavior,
disabled/enabled overhead, request/reply load, sustained bounded-load and retained-state
recovery, a fresh isolated 81-package candidate and final handoff remain open.
Earlier benchmark/regression snapshots are historical after this production edit;
they were not rerun or rewritten to appear current. Nothing was published and no
external consumer was contacted. The goal is active, not release-ready.

## Previous checkpoint: canonical graph condition measurements (2026-09-10)

Permanent coverage now compares unconditional, native and both configured expression
paths on actual Core routes: 1/8/32 forwarding nodes, two targets, original-envelope
fanout and explicit fallback. Portable parsing/compilation stays in setup; actual
per-message document binding is measured. The benchmark project targets both supported
frameworks. No production API, delivery policy, component creator or runtime was added.

All 24 Dry cases, 24 Default matrix cases, one representative and four repeated
8-hop cases completed. All 873 captured source/project/runner files remained unchanged.
Native predicates add effectively no allocation; portable evaluator costs are now
measured separately. This is a candidate baseline, not a before/after speedup or load
latency claim. See artifacts/core-refactoring/graph-condition-measurement.md and
graph-condition-final-verification.json for exact figures, commands and caveats.

The full nonincremental Release build passed in 39.18 seconds with zero warnings/errors.
Four focused benchmark convention cases and all 252 release checks passed. The runner
inventory now covers 77 cases per framework/job, not all measured in this checkpoint.
The broader 4,112-case regression result below was not rerun in this benchmark-only
slice. Assertion/gap review is recorded in .testagent/graph-condition-review.md.

Telemetry review found a concrete remaining defect: diagnostic metrics include trace
identity. Synthetic diagnostic-receipt activities also do not establish operation-span
or context-propagation correctness. See artifacts/core-refactoring/telemetry-qualification-gap.md.
Telemetry correction/measurement, shared-trace request/reply, sustained bounded-load
and retained-state recovery, a fresh isolated 81-package candidate, and final handoff
remain open. No package was published and no external consumer was contacted.

## Previous checkpoint: explicit output policies (2026-09-10)

Portable DiscardUnmatched/RepliesFrom, fluent marking, shared strict route completion
and revision-owned policy selection are verified across dependent families, durability,
health and local samples. Typed document options exclude only the graph-policy fields
while retaining strict unknown-option validation. A real hosted output failure hang
was fixed by observing existing dispatcher completion through the shared lifetime;
disposed revisions no longer observe later failures of reused outputs.

The preceding qualified snapshot passed 4,112 selected cases, including 252 release
checks, with 4,322 discovered names, one separate local acceptance, zero build warnings
or errors, and 1,599 stable captured files. It was revalidated before the benchmark
changes in this checkpoint. Evidence remains in output-policy-verification.json and
.testagent/output-policy-followup-review.md. Later benchmark documentation/convention
changes mean that historical whole-snapshot verifier is no longer current-tree proof.

## Previous checkpoint: portable fallback routing (2026-09-10)

The flat `Fallback` link flag and `OtherwiseTo`/`OtherwiseFrom` preserve exclusive
unmatched routing across optional authoring, JSON, direct native/contract activation,
editable persistence and revision snapshots. Both execution paths use the same
small Core route-selection helper; delivery stays in the existing native blocks.
No new component creator, public runtime abstraction or per-output queue was added.
One fallback per output is enforced before activation and in combined route
selection. A reproduced revision defect was fixed: invalid candidate routing can
no longer attach candidate sources before conflict validation.

Final nonincremental Release build: 22.39 seconds, zero warnings/errors. All 4056
selected regression cases passed, including 252 release checks, plus one separate
local acceptance. Serial discovery found 4266 cases. The checkpoint adds 34 cases;
31 captured source/test/API/document files remained stable and whitespace-clean.
All final qualification processes exited zero. Earlier focused assertion and exact
API convention failures remain recorded alongside the real activation regression.
Evidence: artifacts/core-refactoring/fallback-routing-verification.json,
fallback-routing-verify.ps1 and .testagent/fallback-routing-review.md.

This completes fallback propagation and business-route selection, not complete
routing-policy parity. Explicit discard, terminal outputs and reply handling are
the next convergence gate. Wider performance measurements, a fresh isolated local
81-package candidate and final feature/package handoff remain required. Nothing
was published or coordinated with an external consumer.

## Previous checkpoint: revision drain failure coordination (2026-09-10)

Optional revision drains now route escaping callback/wait failures through the
same Core FlowLifetime coordinator as ordinary execution. Staged stop and final
completion failures, plus staged/fallback cancellation, previously escaped without
faulting every component. The fix preserves the original error, graceful stage
order and explicit disposal ownership; a failing Fault callback cannot strand a
sibling. No new production abstraction, queue, public API or dependency was added.

Six new cases cover those boundaries and successful production/full-completion
ordering. Four failed against the unchanged production code. The final Release
build passed in 22.84 seconds with zero warnings/errors; 4022 selected regression
cases passed, including all 252 release checks, with zero failures/not executed;
one separate local acceptance case passed; serial discovery found 4232 cases.
All 19 captured source/test/audit/changelog files remained stable and free of
trailing whitespace. All qualification processes reached terminal exit code zero.
Evidence: artifacts/core-refactoring/revision-drain-verification.json,
revision-drain-verify.ps1 and .testagent/revision-drain-review.md.

The update-runtime audit now identifies a concrete remaining gate, not just a
generic convergence task: unmatched/fallback business-routing semantics still
differ between Core FlowRouter and optional ApplicationOutputPort. The portable
link contract lacks the corresponding fallback/discard representation. Preserve
legitimate terminal outputs, replies, capture and observations while carrying the
approved explicit routing policy through authoring and revision snapshots. See
2026-09-10-update-runtime-convergence.md for current source boundaries and the
next bounded implementation. Shared fanout/lifetime helpers alone do not prove
complete routing-policy parity.

Wider condition/telemetry and sustained-load performance, a fresh isolated local
package candidate, and final handoff remain open. Earlier performance evidence is
historical, not rerun here. No external-provider acceptance, publication or
external consumer coordination is claimed. The goal remains active.

## Previous checkpoint: remaining standard family registrations (2026-09-10)

All 27 remaining standard registration candidates across 16 families now reuse
HasStandardPorts, bringing the total to 33 standard family registrations. Their
48 standard-name constants reference Core names without changing compiled values.
FlowTransformNode uses CreateStandard; FlowSource shares OutputName. Constructors,
materializers, options/resources, processing profiles and activation modes are
unchanged. Source-only, join, retry and trigger/control bindings remain explicit;
no dummy ports, new creator layer, runtime queue or production API was added.

The exact source-diff verifier confirms that the 16 registration files changed
only by replacing the 27 selector triplets with the same shape expressions. It
also confirms the 48 alias changes/formatting and no unrelated definition edits,
including unchanged custom constants. The public declaration baseline changes in
16 composition packages reflect constant initializers and formatting, not changed
constant values or new public members. Unpublished version choices remain in place.

Eight new cases verify async plain/owned factories, metadata without construction,
pending activation, native block/envelope identity, completion selection, and all
node/resource cleanup success/failure combinations. One stale source convention
was replaced with compiled port-name validation against actual registered
descriptors; all 38 affected convention/API cases pass. The first failing log is
preserved. No processing test expectation or exclusion was loosened.

Final qualification: nonincremental Release build in 33.91 seconds, zero warnings
or errors; 4016 selected regression cases passed, zero failed/not executed;
79 completed reports (five filtered empty), including 252 release checks; one
separate local workflow acceptance case; 4226 sequentially discovered cases.
All 38 captured source/test/API/changelog files stayed stable and whitespace-clean.
Evidence: artifacts/core-refactoring/family-standard-ports-verification.json,
family-standard-ports-verify.ps1 and .testagent/family-standard-ports-review.md.

The standard-registration migration is complete. Custom port declarations remain
explicit by design. Remaining update-runtime convergence, wider condition/telemetry
and sustained-load performance gates, and a fresh isolated package candidate still
prevent full-goal completion. No performance rerun, external-provider acceptance,
package publication or external consumer coordination is claimed.

## Previous checkpoint: shared standard port binding (2026-09-10)

Core now shares the Input/Output/Events names and the non-owning
FlowPorts.CreateStandard view. FlowNode and direct endpoint conveniences reuse
those names. Optional HasStandardPorts binds an IFlowComponent's native ports
without constructing it during metadata inspection. It rejects missing, renamed,
additional and control ports; explicit custom bindings remain available. This
adds no runtime queue, component-creator hierarchy or direct-use registration.

All six serialization registrations now use the shortcut with their existing
materializer shapes. Constructors, conversion, options/resources and event bridge
semantics are unchanged. Core has four additive declarations and Composition two;
serialization's existing constants retain their values but reference the shared
names. Its source-declaration baseline count also changes because the formerly
single-line declaration is now multiline. Existing unpublished version selections
remain; no new published version or package candidate is claimed.

Verified: 23 new cases; 4008 selected regression cases passed with zero failed or
not executed, 79 completed reports (five filtered empty), including 252 release
checks; one separate local workflow acceptance case. Final nonincremental Release
build took 21.86 seconds with zero warnings/errors. All 14 captured source/test/API
and usage-document files remained stable and free of trailing whitespace.
Evidence: artifacts/core-refactoring/standard-ports-verification.json and
.testagent/standard-ports-review.md, including exact requirement/test mappings.

The first full run exposed three stale source conventions. They now accept the
standard event contract, inspect compiled port constant values and verify the
current 65-case benchmark suite/runner. The affected 40-case scope passes; initial
failing logs remain. The first parallel discovery log interleaved two names;
sequential discovery lists 4218 cases, including all 23 new cases.
No new runtime performance measurement is claimed by these convention repairs.

This is the first registration family, not framework-wide convergence. A syntax
inventory finds 27 further shortcut candidates across 16 family registration
files; each still needs native-shape/control/cardinality and activation review.
Remaining update-runtime convergence, condition/telemetry and sustained-load
performance gates, and a fresh isolated package candidate are open. The earlier
benchmark measurements are historical snapshots, not reruns of this source state.
Nothing was published and no external consumer/provider acceptance was claimed.

## Previous checkpoint: payload-size forwarding measurement

Permanent coverage now measures ordinary instance-first Core application delivery
for 1/8/32 hops, 1/2/8 targets, structured/binary content and 1 KiB/1 MiB sizes.
Pre/post probes at every target verify original envelope/value references and
complete content after caller-owned arrays are changed. Safe ingress copies and
materialization are outside timing; no production runtime code was changed.

Final clean benchmark build: zero warnings/errors. Verified 36 final Dry cases,
36 full Default cases, a separate representative pair and four targeted repeats;
all case processes exited zero. All 48 captured Core/benchmark source files and
the existing power plan stayed unchanged. Full matrix execution was 782.99 seconds
(797.69 globally). An initial nullable warning was fixed and all smoke cases rerun
before measurement; the earlier artifacts remain preserved.

Across all 18 size pairs, increasing payload size from 1 KiB to 1 MiB changed
allocations by -11 to +38 bytes/round trip, evidence against payload-sized copies
on the measured forwarding/fanout path. At 32 hops/eight targets the one-MiB cases
allocated 7329 bytes (structured) and 7333 bytes (binary). Two apparent timing
gaps did not reproduce: structured 8-hop/1-target repeated at 18.623/18.969 us;
binary 32-hop/2-target at 59.841/59.917 us. Some cases required 98-100 iterations;
uncertainty is retained, not a claim of invariant latency or general speedup.

Evidence: artifacts/core-refactoring/payload-size-final-verification.json and
payload-size-measurement.md. The permanent benchmark guide now describes all
65 configured cases without claiming they were all measured in this checkpoint.
performance-gates.md separates remaining evaluator/telemetry, sustained-load,
tail-latency, retained-state and numeric-budget work. Earlier native allocations
remain above the weaker-protocol baseline. Registration/update convergence and a
fresh isolated package candidate are still open. No unit-suite rerun, publication
or external-provider acceptance is claimed by this benchmark-only checkpoint.

## Previous checkpoint: public delivery API measurement

The permanent delivery benchmark now compares actual FlowApplication construction
and public SendAsync against native links with identical node/target types,
capacities, small structured payload and 1/8/32-hop by 1/2/8-target matrix.
All setup probes verify reference-identical fanout. No production runtime change
was made for this checkpoint; the graph reuses the existing output dispatcher.

Verified 18 Dry cases, a separate two-case Default repeat and all 18 Default
cases; zero-warning/error Release benchmark build; all 47 captured source files
stable. The full measured run took 361.10 seconds, 376.17 seconds globally.
Application means and allocations are lower in all nine side-by-side pairs.
At eight hops/two targets: 19.325 us and 2398 bytes versus native 27.786 us and
7677 bytes. The representative repeat was consistent. Native allocations still
exceed the original baseline, whose source protocol provided weaker guarantees.
This tradeoff remains explicit, not silently accepted.

Evidence: artifacts/core-refactoring/delivery-api-verification.json and
delivery-api-measurement.md. Earlier access-failed and misconfigured runs are
retained and excluded. The final runner configuration preserves CLI job selection
and the existing power plan. No unit-test rerun or production speedup is claimed.

Payload scaling, conditions/telemetry, sustained load and tail-latency gates remain
open, alongside remaining registration/update convergence and the fresh isolated
local package candidate. Nothing was published.

## Previous checkpoint: shared native control identity (2026-09-09)

Direct contract construction and update-time drain planning now use the same
Core FlowPorts classification of terminal controls by native block identity.
Previously the update drain planner matched bound names against native names;
renaming Ack to Terminal caused a reproduced shutdown timeout. The new shared
internal check preserves aliases and does not mistake a business input named Ack
or marked Signal for a terminal control. No public API, factory, dependency or
delivery queue was added. Ordinary constructors remain unchanged.

The pre-fix host test passed Ack and failed Terminal. After the fix both pass,
including held-work/control ordering, original reply identity and exactly-once
owned disposal. Three projection cases cover native controls, no controls and
raw-node fallback, with misleading names and mixed signal/message metadata.

Final qualification: nonincremental Release build in 33.59 seconds, zero warnings
and errors; 3985 selected regression cases passed, zero failed/not executed;
79 completed reports (74 nonempty), including 252 release checks; 4195 discovered
names; all five scoped cases discovered and passing; one separate local workflow
acceptance case. All five captured source/test files stayed stable and passed
literal whitespace checks. Every final verification process exited successfully.
Evidence: artifacts/core-refactoring/control-identity-verification.json and
.testagent/control-identity-review.md; earlier failing logs remain preserved.

This is a correctness/convergence checkpoint, not completion of the whole goal.
Metadata/registration simplification, remaining update-runtime convergence,
performance qualification and a fresh isolated local package candidate remain
open. No external provider readiness, performance gain or publication is claimed.

## Previous checkpoint: direct contract activation into Core (2026-09-09)

ApplicationSetup, ApplicationDefinition and the authoring builder can now build
the ordinary FlowApplication using existing component/resource contracts and
application services. No duplicate ComponentBindings inventory or update runtime
is required. Direct instance constructors remain unchanged. Typed caller predicates
materialize from the canonical value without replacing the transported envelope.

Resource registration is shared through the public RegisterResources boundary;
physical-port validation is shared through the existing descriptor factory.
Revision snapshots stay in Engine, with no production friend access to Composition.
The first regression caught an attempted boundary violation and that design was
corrected without changing the release guard. Core receives only internal lifetime
ownership and native node identity; no DI dependency or new delivery queue is added.

The graph owns constructed components and then their resource scope. Tests cover
host borrowing, extra activation cleanup, renamed terminal controls, unstarted
source disposal, native failure under composed completion, typed and named-engine
conditions, event routes, isolated rejection, cancellation, retained failures and
sequential/concurrent repeated factory results. The composition sample no longer
references Engine and both C# and JSON modes produce exactly ALPHA and BETA.

Final qualification: non-incremental Release build in 20.14 seconds with zero
warnings/errors; 3981 selected regression cases passed, including 252 release
checks; 79 completed reports (74 nonempty); 4191 discovered test names; 31 new cases
all discovered and passing; one separate local acceptance case; 47 stable source/API
and documentation states. Focused Composition/Engine runs passed 238/321 cases.
Scoped whitespace checks passed. Earlier failed logs remain preserved.

Evidence: artifacts/core-refactoring/direct-activation-verification.json and
.testagent/direct-activation-review.md. The API baseline adds the optional overloads
and resource-registration boundary; Engine's baseline returns to its preceding
value. Lexical declarations include internal-type members and are not a binary
compatibility claim. This slice adds construction/lifetime integration; it does
not claim a net line-count reduction or measured performance improvement.

Still open: component-local metadata/registration simplification, remaining
update-runtime convergence, complete performance matrix, and a fresh isolated
package candidate with package-only consumers. Provider-dependent external tests
remain separate. No publication, external consumer coordination, context-aware
Add or unapproved feature additions occurred.

## Previous checkpoint: canonical component declarations (2026-09-09)

Physical component ports now use one sealed ComponentInputPort and one sealed
ComponentOutputPort, holding native canonical blocks. Direct constructors and
ComponentPorts.Input/SignalInput/Output expose the same target/source. Removed the
typed/FlowValue-specific subclasses, duplicate declaration aliases and unused
internal completion/fault helpers. One native input binder serves both business
and terminal-control declarations; output and event roles stay distinct.

Optional Composition/Designer registration uses HasInput and HasOutput, retaining
shape and cardinality overloads. All 19 real family registration files in this
scope, samples, benchmark fixture and package-consumer sources are migrated.
Typed caller handles/predicates remain projections over canonical physical blocks.
This does not introduce a component creator requirement into direct Core use.

Preserved metadata-only discovery, exact selected block identity, role order,
named event sources, cleanup, shape preflight, optional presentation metadata and
runtime control ownership. Impossible typed physical mismatch fixtures now test
invalid structural metadata or inverse semantic-kind mismatch. All five advanced
instance mismatch scenarios remain. Original files are recoverable from
canonical-declarations-originals.json and canonical-declarations-benchmark-original.json.

Twenty-seven captured production files decrease from 3037 to 2821 lines: 216 fewer.
Public source-surface baselines were deliberately reviewed for Composition and
Designer; Core and Engine rows did not change. These are lexical surface checks,
not binary compatibility certification.

Five new test methods add 14 cases: constructor/factory, business/control and
success/error combinations; guards and sealed native API; omitted versus explicit
null input shapes in both authoring packages. Existing boundary/metadata fixtures
were migrated, not skipped. Static assertion and pseudo-mutation review identified
and closed the explicit-null-shape gap. Initial compile/build failures are retained.

Final qualification (all four processes exited zero):
- Non-incremental Release solution build: 21.69 seconds, zero warnings/errors.
- Selected regression: 3950 passed, zero failed/skipped; 79 completed reports,
  74 nonempty and five filter-empty. Includes 252 release checks.
- Discovery: 4160 names; all 14 added cases are present.
- Fresh local workflow acceptance execution: one passed.
- Focused Composition 207, Designer 178, Engine 50 and durable-input 163 passed.
- All 72 captured source/API/document states remained unchanged; scoped tracked
  diff check and explicit new-test whitespace scan reported no findings.

Authoritative report: artifacts/core-refactoring/canonical-declarations-verification.json.
Test review: .testagent/canonical-declarations-review.md. Historical graph query
used vocabulary [typed, binding, port, registration], 21 nodes, no extraction;
live source and execution results are the implementation evidence.

The full goal remains active. Remaining activation/resource-fact consolidation,
authoring/operations convergence, the complete feature/performance matrix and
fresh isolated package-only consumer qualification are not certified by this
checkpoint. No external service integration or new throughput/heap claim.
No package publication, tags, pushes or external consumer coordination occurred.

## Previous checkpoint: native control inputs (2026-09-09)

Removed IFlowSignalTarget, NativeFlowSignalTarget and ApplicationSignalInputPort.
Controls expose their native ITargetBlock<FlowMessage>; registration, optional
designer bindings, revision replacement and keyed DI consume that same contract.
Signal handles/metadata remain terminal-lifetime descriptions, not a second
transport. Both kinds now share ApplicationInputPort, one canonical native
dispatcher and one revision-delivery path. Typed callers convert once before
admission. Original canonical message/value/header references stay intact.

The generic input dispatcher, injected dispatch/identity delegates and allocated
identity record are removed. The no-caller Composition TryLinkTo hierarchy and
both signal forwarders are removed, not replaced with another execution path.
Nineteen captured production files fall from 4951 to 4439 lines (512 fewer),
including three deleted files. All originals are recoverable from
signal-inputs-originals.json and signal-inputs-core-original.json.

Ownership is preserved: business input completion completes and joins its target;
terminal-control input completion, host abort and revision retirement do not close
component-owned controls. Existing revision admission/retained-route/rollback,
durability and component lifecycle behavior remains. Feedback that produces data
is still a business input; no new cycle exemption or iteration feature was added.

Four new methods/seven cases cover native view postponement, exact message/error
identity, cancellation/capacity, and control ownership through completion/abort
and revision handover. The existing saturation/revision theory now covers both
kinds, adding two cases. The lower-level dispatcher fixture was migrated from
injected send delegates to native offer/consume/reservation behavior. Initial
fixture probe-count and postponed-decline failures are preserved; production
semantics and existing assertions were not weakened. Assertion/gap review records
fifty explicit assertion callsites across the four new methods.

Final nonincremental Release build: 33.15 seconds, zero warnings/errors.
Focused checks passed: Engine 147, Composition 47, Core controls 16 (eight on each
target framework), Designer 8, durable intake 163. Final selected regression:
3936 passed, zero failed/skipped; 79 Completed reports, 74 nonempty and five
filtered-empty, including all 252 release checks. Discovery: 4146 names, nine
more than the prior checkpoint. Seven new plus four expanded revision cases
were individually verified in discovery and passing results. Fresh separate
local workflow acceptance: one passed. All four final processes exited zero.
All 35 scoped source/API states remained stable. Whitespace checks passed.

Reviewed lexical API inventory: Core 415 -> 414; Composition 472 -> 455;
Engine 414 -> 383; Designer 300 -> 299. All four baselines match the final source.
This scanner includes public members of internal types; it is not binary API
compatibility proof. Migration notes explain native target resolution and the
removal of generic CLR signal-envelope identity. See .testagent/signal-inputs-review.md
and artifacts/core-refactoring/signal-inputs-verification.json for exact evidence.

Architecture guidance kept one transport and explicit lifetime ownership. Test
guidance required native protocol assertions, kind parity, source/test pairing,
and documented fixture corrections. Historical graph retrieval expanded
[signal, input, port, target], DFS depth two over 34 nodes. TargetBlockView at
src/FluxFlow.Core/Configuration/DependencyInjection/NonOwningDataflowViews.cs:L15
located the old boundary; the graph was not rebuilt and does not prove current
source behavior. No extraction ran (zero input/output extraction tokens).

Still open: remaining typed physical Composition declarations and component-local
activation/resource consolidation; complete family inventory and authoring/runtime
convergence; the full feature/performance matrix; fresh isolated package-only
qualification. No publication or external consumer coordination. Context-aware
Add and unapproved templates/iteration remain deferred. Overall goal remains active.


## Previous checkpoint: canonical retry inputs (2026-09-09)

FlowRetryNode is now one public canonical IFlowComponent. Its actual Input, Ack,
Nak and Cancel blocks are bounded native message inputs, not custom signal targets
or terminal controls. The generic public wrapper, internal canonical duplicate,
copied retry-input envelope and intermediate transform queue are removed. Across
the four captured production files, 827 lines become 679 (148 fewer). Deleted
FlowValueRetryNode and RetrySignalTarget are recoverable from retry-inputs-originals.json.
No Core production/API change, new project, package or compatibility layer was added.

Feedback admission uses native SendAsync semantics. Matching still uses trace plus
the private per-component attempt header; stale feedback is reported as ignored.
Matched Ack/Cancel terminal processing now belongs to its native handler lifetime.
Completion joins admitted feedback and its fault supervisors before stopping pending
attempts, then drains results and releases timers. This prevents matched but queued
terminal processing from racing shutdown. Faults close sibling inputs and preserve
the initiating cause. Native input completion is not a component-completion alias.

Optional document activation constructs the same public node and advertises canonical
input handles. All three feedback inputs publish an immutable Any payload shape with
no required properties. Runtime and designer tests verify its seven accepted JSON
kinds. Existing graph cycle rejection remains, explicitly tested for all three ports.
Documentation records the breaking admission/settlement distinction and local typed
RetrySignal materialization. No arbitrary iteration/feedback feature is introduced.

Six new test methods/thirteen cases cover real native linking, structured nested and
byte-array payloads, error feedback, upstream errors, active plus queued valid
acknowledgements during stop, saturation cancellation, five fault origins, addressed
direct graph execution and cycle rejection. Existing retry/hosted scenarios remain;
no test cases were deleted. Assertion/gap review records 78 explicit new callsites.
The first full regression exposed missing feedback-shape metadata, which was fixed
without weakening the release check. Its 3926-pass/one-failure report is preserved.

Fresh nonincremental Release build: 22.53 seconds, zero warnings/errors. Narrow
qualification: 35 retry, 8 composition and 14 registration-matrix tests passed.
Final selected regression: 3,927 passed, zero failed/skipped; 79 Completed reports
(74 nonempty, five deliberately filtered-empty), including all 252 release checks
and all thirteen new cases. Discovery: 4,137 names, thirteen more than the previous
checkpoint. Fresh local acceptance: one passed. All four final processes exited
zero and all fifteen scoped source/API states stayed unchanged. Scoped diff and
new-file whitespace checks passed. See .testagent/retry-inputs-review.md and
artifacts/core-refactoring/retry-inputs-verification.json for exact evidence.

Reviewed lexical API inventory: Resilience index 3 changes 90 -> 77; composition
index 4 remains 48 with canonical handle signatures; Core stays 415 and unchanged.
These are source-shape inventories, not binary compatibility proof. Architecture
guidance kept the native processing boundary inside the module; test guidance made
queued feedback ownership and optional authoring metadata explicit.

Historical Core graph query used [retry, feedback, signal, completion], DFS depth
two, 23 nodes. It located RetryStateMachine at
src/FluxFlow.Resilience/RetryStateMachine.cs:L54 but does not describe
the new module implementation. No extraction ran (zero input/output extraction tokens).

Still open: component-local activation/resource consolidation and complete family
inventory; direct/fluent/document/revision runtime convergence; full feature and
performance matrix; a fresh isolated local package feed and package-only consumers.
No publication or external consumer coordination. Context-aware Add and unapproved
template/iteration features remain deferred. The overall goal remains active.

## Previous checkpoint: retire the unused correlation tracker (2026-09-09)

Removed CorrelatedRequestTracker, its options/start-result types, the no-op bridge
SweepInterval option and unused duplicate-correlation diagnostic. Remaining error
numbers are unchanged. The bridge's duplicate constructor option checks are gone:
the sealed immutable options validate on assignment and record updates. Its only
settings are Mode, Timeout and Capacity. No replacement callback registry, runtime,
Core API, project or package was added. Four production files shrink from 862 to
489 physical lines (373 fewer). Both deleted production files and the retired
test file are recoverable from tracker-retirement-originals.json.

Core FlowRequestReply remains the graph exchange API. Explicit keyed consumers
use existing PendingExchangeCoordinator settlement/context/batch operations and
await their own callback tasks/tokens. Transport adapters keep the bridge's
automatic bounded callback lifetime. Migration docs explicitly describe that
breaking ownership change, canceled context access and protocol key validation;
they do not claim Core invokes or awaits arbitrary consumer callbacks.

Every retired wrapper test (14 methods / 25 cases) has a replacement/semantics row
in .testagent/tracker-retirement-migration.md. Six new cases verify independent
bulk batches while a consumer remains held, cancellation/context identity, required
arguments without lost capacity, failed reply callback isolation and effective
flat options. The options test ran red before removing the sweep field. Existing
Core and bridge tests retain deadline, capacity, timer-failure and cleanup coverage.

The first full run exposed a prior hosted-stop test race: callback entry did not
prove output had drained. The refined test awaits production, response-control and
consumer completion before canceling the host wait. Its strict success/ownership
assertions are retained; it does not accept either terminal result. The initial
failure and test original are archived. Both refined variants pass at full scope.

Final nonincremental Release build: 24.28 seconds, zero warnings/errors. Selected
regression: 3,914 passed, zero failed/skipped; 79 Completed reports (74 nonempty,
five intentionally filtered-empty), including 252 release checks, all six new
cases and both refined variants. Discovery: 4,124 names. Local acceptance: one
passed. Narrow: 45 coordination, 48 bridge and 22 HTTP. All final processes
terminal zero. Eleven scoped source/API states remained unchanged; scoped diff
and new-test whitespace checks passed. Active-source search finds no retired API.

Reviewed lexical API row: RequestReply index 8, 49 -> 35; Core 415, trigger 30
and outbound composition 31 unchanged. Counts are source-shape checks, not binary
compatibility proof. Evidence: artifacts/core-refactoring/tracker-retirement-verification.json
and .testagent/tracker-retirement-review.md. Architecture guidance removed the
compatibility callback layer instead of moving it into Core; testing guidance
preserved the behavior map and corrected the nondeterministic drain fixture.
Historical Core graph navigation: DFS depth two, fifteen nodes, no extraction
(zero input/output extraction tokens), not current module relationship evidence.

Still open: retry's non-terminal signal semantics; component-local activation and
resource consolidation; shared direct/fluent/document/revision runtime; complete
feature/performance matrix; fresh isolated local package feed and package-only
consumer qualification. No publication or external coordination. Context-aware
Add and unapproved template/feedback remain deferred. Full goal remains active.

## Previous checkpoint: shared inbound exchanges and single trigger ingress (2026-09-09)

The transport bridge now delegates matching, deadlines and exchange identity to
Core FlowRequestReply. Graph messages get fresh exchange correlation plus expected
reply route; transport contexts retain their external IDs, including concurrent
reuse. A bounded set owns caller tasks only, not a second matching registry.
Response materialization happens once after canonical matching. Caller capacity
includes slow terminal callbacks even after pending exchange count becomes zero.

Unexpected request getter/serialization failures close admission and settle every
accepted queued caller. Diagnostic failures after reply, error, timeout or capacity
notification cannot notify the caller again. Core Reply returns settlement status
for unmatched diagnostics. Production-only drain remains separate from terminal
response control shutdown, and full completion joins owned callbacks and cleanup.

HttpTriggerNode exposes the actual bounded Incoming target. Removed HttpTriggerSource,
its keyed registration, source constructor argument and redundant link. Endpoints
feed the node directly. Hosted stop closes this input and awaits callbacks; canceled
host waits retain node ownership of running notifications. Existing accepted-failure
500 and rejected-admission 503 mapping is unchanged. The deleted 38-line source is
recoverable from bridge-exchanges-originals.json. Across seven captured production
files, physical lines fall from 955 to 815 (140 fewer).

Fourteen new cases cover stale replies after success/timeout, concurrent external
IDs, exact results, queued extraction/serialization failure, callback capacity,
diagnostic fault boundaries, trigger stop/fault and canceled hosted stop waits.
The original duplicate-notification regression failed before the fix; its red log
is retained. Review strengthened terminal and host cancellation coverage without
adding a test stack, changing response mappings or suppressing warnings.

Final nonincremental Release build: 35.27 seconds, zero warnings/errors. Selected
regression: 3,933 passed, zero failed/skipped; 79 Completed reports (74 nonempty,
five intentionally filtered-empty), including 252 release checks and all fourteen
new cases. Serial discovery: 4,143 names. Separate local workflow acceptance: one
passed. Narrow: eighteen Core executions, 71 bridge and 22 trigger cases. All
final processes terminal zero; sixteen scoped source/API states stayed unchanged.
Scoped diff/new-test whitespace checks passed.

Reviewed lexical API: Core index 0 still 415 with Reply's return-type change;
Http.AspNetCore index 9, 34 -> 30; bridge index 8 unchanged 49 and outbound
composition index 34 unchanged 31. Counts include internal-type declarations and
exclude inherited members, not binary compatibility proof. Package README and
major migration notes explain exchange IDs, direct ingress, bounds and ownership.

Evidence: artifacts/core-refactoring/bridge-exchanges-verification.json and
.testagent/bridge-exchanges-review.md. Architecture guidance kept matching in
Core and transport lifetime at the boundary. Testing guidance added red/green
terminal-notification and canceled-host-wait checks. Historical Core navigation:
DFS depth two, eleven nodes; no extraction (zero input/output extraction tokens).

Still open: retire the unused public CorrelatedRequestTracker utility and no-op
SweepInterval while preserving required behavior; retry's non-terminal signals;
component-local activation/resource consolidation; shared direct/fluent/document/
revision runtime; complete feature and performance matrix; fresh isolated local
81-package feed and package-only consumer qualification. No publication or external
coordination. Context-aware Add and unapproved template/feedback remain deferred.

## Previous checkpoint: direct file sources and inbound bridges (2026-09-09)

DirectoryEnumerateNode and FileWatchNode now inherit FlowSource, removing their
duplicate source pipeline. Their options, path policies, records and watcher
diagnostics remain. RequestReplyCoordinator and HttpTriggerNode expose the actual
canonical Output/Responses blocks through source/control Ports and support direct
workflow.Add. Request contexts remain typed at the transport boundary. The trigger
no longer inserts conversion/output/relay action queues; its reply materializer
runs inside the existing bridge response action.

Explicit startup separates wiring from execution. Production-only stop closes
incoming admission and drains output while terminal response controls remain open
for business work to finish. Full bridge shutdown settles queued pre-start callers.
No new Core runtime, creator, project or package was added. Across six captured
production files, physical lines fall from 1,585 to 1,374 (211 fewer). The removed
150-line FileSystemSourcePipeline is recoverable from remaining-components-originals.json.

Fourteen new direct-graph cases cover ports, real file/watch records, stop/fault,
backpressured source fault, pre-start closure, canceled startup retry, reply drain,
exact binary replies, caller-local invalid shape, and trigger validation/diagnostics.
Existing typed bridge tests now exercise canonical graph values and explicit start.
Test review added the blocked-source regression. Native-handle leak measurement
and exhaustive startup race proof are not claimed; limitations are recorded.

Final nonincremental Release build: 35.16 seconds, zero warnings/errors. Selected
regression: 3,919 passed, zero failed/skipped; 79 Completed reports (74 nonempty,
five intentionally filtered-empty), including 252 release checks and all fourteen
new cases. Serial discovery: 4,129 names. Local workflow acceptance: one passed.
Narrow suites: 51 filesystem, 61 bridge, 18 trigger. All final processes terminal
zero. Fourteen scoped source/API states stayed unchanged across qualification;
scoped diff/whitespace checks passed.

Reviewed lexical API changes: RequestReply index 8, 45 -> 49; Http.AspNetCore index
9, 30 -> 34; FileSystem index 25, 202 -> 179. Related composition indices 26 and 34
unchanged. These source-shape counts include internal declarations and exclude
inherited members; they are not binary-compatibility proof. Migration/package
notes explain canonical values, explicit startup, response controls and host ownership.

Evidence: artifacts/core-refactoring/remaining-components-verification.json and
.testagent/remaining-components-review.md. Architecture guidance favored shared
lifecycle ownership; testing guidance added explicit drain/fault boundary cases.
Historical Core graph navigation: DFS depth 2, 19 nodes, no extraction (0/0 tokens).

Still open: unique bridge exchange identity for reused external correlation IDs;
unexpected ingress fault and separately host-buffered caller settlement audit;
retry's non-terminal signal migration; component-local activation/resource
consolidation; shared direct/fluent/document/revision runtime; full feature and
performance matrix; fresh isolated 81-package local feed and package-only consumer
qualification. Hosts retain external request-source ownership. No publication or
external coordination. Context-aware Add and unapproved template/feedback remain deferred.

## Previous checkpoint: canonical timer components (2026-09-09)

All five public timer nodes now support constructor-first Core graphs. Delay,
throttle and debounce directly inherit FlowNode; interval/schedule forward their
existing source port inventories with Output/Events only. Optional registration
constructs the same public classes. Removed generic operator variants, three
duplicate canonical classes, duplicate support and the separate timer pipeline.
The eighteen captured production files shrink from 2,733 to 1,744 physical lines:
989 fewer, excluding tests/docs. Five removed files are recoverable from the
checkpoint originals archive. Domain-required timing queues remain bounded.

Pure timing and upstream-error forwarding retain the exact original message;
new operation failures still derive error results. Input capacity now counts the
executing delegate as well as queued input. Timing settings, source tick shapes,
clock ownership and flat optional configuration remain. No Core change, runtime,
creator API, project or compatibility shim was added.

Six new direct-graph tests cover actual ports for all five nodes, scheduled ticks,
binary/error identity, debounce stop flushing, pending delay fault cleanup,
pending debounce fault cleanup and bounded throttle admission/cancellation. Review
found an unconnected debounce disposal hook: the new regression failed with one
live timer after disposal. The hook now disposes the timer, clears pending input
and joins emissions; the red report is preserved. The schedule test also uses an
independent clock so its registration signal cannot be confused with debounce.

Final nonincremental Release build: 34.33 seconds, zero warnings/errors. Selected
regression: 3,905 passed, zero failed/skipped, 79 Completed reports (74 nonempty,
five intentionally filtered-empty), including 252 release checks and all six new
tests. Serial discovery: 4,115 names. Local workflow acceptance: one passed.
Reviewed narrow suites: 77 runtime and 14 optional-registration cases. All final
processes terminal zero; all thirty scoped states (25 files, five deletions)
unchanged across qualification. Scoped diff/whitespace checks pass.

Explicit lexical API review: Timers index 29, 167 -> 110 declarations;
Timers.Composition index 30 unchanged at 58. This source-shape metric includes
internal-type declarations and excludes inherited members; it is not binary
compatibility proof. Migration/package notes document the generic retirement,
message identity, capacity and cleanup semantics.

Evidence: artifacts/core-refactoring/canonical-timers-verification.json and
.testagent/canonical-timers-review.md. Graph navigation reused the historical Core
index, DFS depth 2/25 nodes, zero extraction input/output tokens. Current module
source and executed tests are authoritative. Test-quality review materially found
the missing fault-disposal hook; the architecture skill favored shared lifecycle
ownership without new abstractions.

Still not release-ready: remaining special wrappers (trigger, retry, request/reply)
and source-port audit; component-local activation/port/resource consolidation;
shared direct/fluent/document/revision runtime; complete feature/performance matrix;
fresh isolated 81-package local candidate and package-only consumer qualification.
Faulted Completion is not claimed to join all auxiliary queues before disposal.
No publication or external coordination. Context-aware Add and unapproved
template/feedback remain deferred.

## Previous checkpoint: direct operation component ports (2026-09-09)

Fourteen existing wrappers now implement IFlowComponent and can be constructed
normally and passed to workflow.Add(name, instance): all six serialization nodes,
file read/write, storage put/get/query/delete and session recorder/query. Ports
forwards the existing inner pipeline inventory. No creator, adapter block, queue,
new descriptor API or runtime was added. This adds two lines per wrapper; the
simplicity improvement is direct construction and a single declared port inventory,
not a production line-count reduction. Constructor, operation and lifetime bodies
match captured originals. Supplied storage/session dependencies remain borrowed.

Serialization pure error forwarding now returns the original message instead of
deriving an unused conversion result and causation edge. Five new direct-graph
tests cover all fourteen port sets, successful family operations, exact upstream
error identity, binary content, lineage, shared-store ownership, session closure,
normal graph completion and reuse after a rejected Add. The existing standalone
error test now enforces the same identity/unchanged-causation contract. Its old
assertion caused one initial module failure; the original and report are retained.
Corrected module suites: 148 passed, no failures/skips.

Final nonincremental Release build: 33.64 seconds, zero warnings/errors. Selected
regression: 3,899 passed, zero failed/skipped, 79 Completed reports (74 nonempty,
five empty under the intentional filter), including 252 release checks, all five
new tests and the updated propagation test. Serial discovery: 4,109 names. Local
acceptance: one passed. All final processes terminal zero; 28 scoped source/test/
API/migration/package-note hashes stayed unchanged across qualification. Scoped
diff and new-test whitespace checks pass.

Four lexical API rows were reviewed explicitly: FileSystem 200 -> 202,
Serialization 106 -> 112, Sessions 199 -> 201 and Storage 246 -> 250. These reflect
fourteen Ports properties plus interface declaration changes; no constructor
signature changes. Source-shape baselines are not binary compatibility proof.
Migration and runtime package notes document direct addition, resource ownership
and the error-forwarding semantic change. The architecture skill favored simple
delegation; test-quality review strengthened intermediate bytes/lineage and query
member assertions. Graph navigation used the historical Core index (DFS depth 2,
22 nodes; zero extraction input/output tokens); current source is authoritative.

Evidence: artifacts/core-refactoring/direct-wrapper-ports-verification.json and
.testagent/direct-wrapper-ports-review.md. This is verified progress, not full
architecture, performance, package-feed or release acceptance. No publication or
external coordination occurred. Remaining special wrappers include the trigger,
three generic timer wrappers, retry and request/reply, plus a separate source-port
audit. Optional activation/port/resource and shared authoring/revision convergence,
the complete feature/performance matrix and a fresh isolated 81-package candidate
remain. Context-aware Add and unapproved template/feedback are still deferred.

## Previous checkpoint: canonical port binding (2026-09-09)

Metadata is now plain structural data. Its constructors and typed convenience
factories describe equivalent ports without carrying a generic dispatch callback.
The host binds canonical message and control blocks directly; five private
visitor classes and the obsolete visitor interface/API are removed. This slice
reduces production source by 116 lines without adding a runtime, factory mode,
queue or compatibility adapter.

New checks exposed a missing canonical overload in the non-owning control DI
view. Direct forwarding now preserves the original envelope and cancellation.
Checks exercise exact delivery through actual bounded blocks, sorted port
addresses/capacities, invalid metadata/physical kinds, snapshot non-ownership and
replacement revisions whose old lease cannot detach the new bindings.

Final Release build: 34.96 seconds, zero warnings/errors. Selected regression:
3,894 passed, zero failed/skipped, 79 Completed reports (74 nonempty, five empty
under the intentional filter), including 252 release checks and 17 new/replacement
cases. Serial discovery: 4,104 names. Local acceptance: one passed. Focused
metadata: 26; host/binding/drain: 46. All final processes terminal zero; 19 scoped
source states unchanged across test qualification. Whitespace checks pass.

Two lexical API rows were reviewed explicitly: Composition 474 -> 472 and Engine
424 -> 414. The public break is the metadata visitor API; Engine's scanner delta
comes from public methods on private classes. Constructor initializer text also
affects the hash. No binary compatibility claim follows from this scanner.
Migration/package notes explain direct metadata inspection. Deleted/edited
originals and the initial failing identity test remain in checkpoint artifacts.

Evidence: artifacts/core-refactoring/canonical-port-binding-verification.json and
.testagent/canonical-port-binding-review.md. This is verified progress, not final
architecture or release acceptance. Component-local activation/port/resource and
shared authoring/revision convergence work, the full feature/performance matrix,
and a fresh isolated 81-package candidate remain. No publication or external
coordination; context-aware Add and unapproved template/feedback remain deferred.

## Previous checkpoint: remaining ordinary module presentation separation (2026-09-09)

FileSystem, Sessions, Timers, Payloads, Projections, Observability, Metrics, State,
Storage and Sources now keep presentation in explicit optional packages. Their
25 runtime contracts retain options/resources/ports and activation/cleanup bodies.
The latter match captured pre-edit source apart from line endings. Six small
immutable metadata edits decorate existing descriptors; no component builder,
factory mode, runtime owner or processing implementation was added.

All 19 ordinary module Composition packages now have zero direct Designer project
references. The four relevant convention checks no longer need migration branches
or allowlists. This satisfies the ordinary-module presentation dependency part of
phase 3; it does not finish all authoring, port or runtime-convergence work.
Ten optional packages bring the manifest and lexical API baseline to 81 entries.
Runtime/authoring and optional presentation documentation, sample consumers,
project wiring and prerelease dependency notes were updated together.

Final non-incremental Release build: 41.85 seconds, zero warnings/errors. Selected
regression: 3,879 passed, zero failed/skipped, 79 Completed reports (74 nonempty and
five empty under the intentional filter), including 252 release checks and all
58 new cases. Local acceptance: one passed. Serial discovery: 4,089 names. Focused
families: 205; metadata helpers: 28; conventions: 80; package/API gates: six;
designer-host suite: 23. All 123 scoped source/project/doc hashes stayed unchanged
through final qualification and the post-process check. Diff/whitespace checks pass.

The initial full regression exposed one missed timer presentation opt-in in a
designer-host fixture. Its explicit optional reference/AddTimersDesign call was
added, then the full build/regression/discovery/acceptance rerun. Earlier missing
message import and internal selector-prefix usage were also corrected. A cached
restore needed approved access to existing package configuration; no global
configuration or feed registration was edited.

Eight existing Composition lexical hashes changed without declaration-count
changes; common Designer adds four classes/six methods, and ten new packages add
two declarations each. All 19 changed baseline rows were reviewed. The existing
scanner still includes initializer text; these checks are not binary API proof.
Tests/helpers and implementations were reopened for assertion/static gap review.

Evidence: artifacts/core-refactoring/remaining-presentation-verification.json and
.testagent/remaining-presentation-review.md. No measured coverage, executed mutation,
performance acceptance or fresh coherent package-feed claim. Nothing was published
or externally coordinated; the full goal stays active and is not release-ready.

Next: finish component-local activation/ports/resources and authoring/revision
convergence; reconcile the complete feature ledger and run the approved performance
matrix; build an isolated coherent 81-package local candidate and package-only
consumer/restart acceptance. Deferred context-aware Add and unapproved template/
feedback capabilities remain outside scope.

## Previous checkpoint: outcome module presentation separation (2026-09-09)

Assertions, Expectations and Resilience runtime registration now uses ordinary
structural contracts without a presentation dependency. Three optional Designer
packages decorate those existing contracts through explicit Add...Design calls.
Descriptor/factory identity, required options/resources, canonical ports and
shapes/cardinality remain intact. Constructor activation bodies are unchanged;
no new component creator or processing/lifetime implementation was introduced.
Palette/default/choice/editor hints remain available to consumers that opt in.

Full non-incremental Release build: 40.34 seconds, zero warnings/errors. Selected
regression: 3,821 passed, zero failed/skipped, 79 Completed reports, including 222
release checks and all nine new cases. Local workflow acceptance: one passed.
Serial discovery: 4,031 names. Focused families: 13/17/8; conventions: 56; final
authoring/manifest checks: nine. Forty-one final hashes unchanged; scoped diff and
trailing-whitespace checks pass. Generated tests/helpers and implementations were
reopened for assertion and static gap review.

The initial full run exposed a stale typed-authoring expectation and missing new
package changelog sections; both were fixed and the full qualification rerun.
Three existing lexical API rows (same declaration counts) and three new package
rows were reviewed and updated. The scanner still includes public initializer
text; this is not binary API verification. Manifest count is now 71 packages.
New presentation packages are unpublished 1.0.0-rc.1; existing versions retained.

Evidence: artifacts/core-refactoring/module-presentation-verification.json and
.testagent/module-presentation-review.md. No measured coverage, executed mutation,
performance acceptance, coherent new package-feed or publication claim. The full
goal remains active and is not release-ready. No external coordination occurred.

Next: separate the remaining ten ordinary module presentation dependencies:
FileSystem, Sessions, Timers, Payloads, Projections, Observability, Metrics, State,
Storage and Sources. Then finish activation/ports/resources and authoring/revision
convergence, approved feature/performance matrices, isolated coherent local
packages, package-only consumer/restart acceptance and release-gate improvements.

## Previous checkpoint: retry and bridge lifetime ownership (2026-09-09)

The retry coordinator now exposes its actual monitor task as completion, handles
faulted attempts and closes adapter input/output after infrastructure failure.
Shutdown cancels between-attempt backoff before joining observations; graceful
terminal results remain intact. Timer disposal failures cannot skip output cleanup.
The outer request/reply bridge now owns shutdown notifications and timer cleanup
through one stable completion task. Admission failures notify the not-yet-tracked
caller; cancellation callback errors survive disposal. Fault cancels backpressured
output without waiting for the consumer. No new factory, owner service or public
callable signature was added; existing public API baseline passes unchanged.

Seven new methods add 26 passing executions. Full non-incremental Release build:
65.72 seconds, zero warnings/errors. Selected regression: 3,812 passed, zero failed
or skipped, 79 Completed reports, including 213 release checks. Separate local
workflow acceptance: one passed. Serial discovery: 4,022 names, all 26 new cases
included. The initial parallel discovery output merged names/banners on the same
lines; its raw line count was not used as the authoritative inventory.

Focused suites passed: retry 22, bridge 55, hosted resilience 8, API baseline 2.
Fourteen final qualification hashes remained unchanged. During the preceding
build only a README wording correction was made; compiled/test source stayed
unchanged. Scoped diff and new-test whitespace checks passed. Tests/helpers were
reopened for assertion and static gap review; no measured coverage, executed
mutation, performance acceptance or new package-feed claim is made.

Evidence: artifacts/core-refactoring/retry-owner-verification.json and
.testagent/retry-owner-review.md. Graceful shutdown still requires linked consumers
to drain or detach. This checkpoint does not introduce a new guarantee for queued
input contexts dropped by native block fault semantics. Nothing was published or
externally coordinated. Full goal remains active, not release-ready.

Next: remove the remaining thirteen ordinary module presentation dependencies,
starting with the related assertion/expectation/retry family, following the
already-established optional presentation package pattern. Continue full
activation/ports/resources and authoring/revision convergence, feature and
performance matrices, isolated coherent local packages and release-gate work.

## Previous checkpoint: module terminal delivery and cleanup (2026-09-09)

The correlated-request adapter now has one terminal notification owner over the
existing coordinator/observation lifetime. Explicit reply/failure, timeout,
infrastructure failure and shutdown no longer split delivery between competing
paths. Caller tokens/errors and silent removal remain intact. Bulk failure joins
only its pending batch, and tracker cleanup still disposes the timer after disarm
failure. Trigger disposal similarly continues through outcome/timer/registration
cleanup without disposing borrowed controller/session instances.

Six new test methods and one expanded theory add 25 executions; all 33 executions
of the seven touched methods passed. Full non-incremental Release build: 68.25
seconds, zero warnings/errors. Final selected regression: 3,786 passed in 79
Completed reports, including 213 release checks. Local workflow acceptance: one
passed. Discovery 3,971 -> 3,996. Thirteen final qualification hashes unchanged;
scoped diff check passed. Focused suites: request/reply 43, trigger module 121.

The first solution run exposed one lexical source-baseline mismatch. Review
confirmed that the scanner consumes implementation text after an inline parameter
attribute. Only package row 8's hash was updated; declaration count remains 45 and
callable signatures/dependencies/versions are unchanged. The corrected final run
passed all release checks. This scanner limitation is recorded for release-gate
improvement, not mistaken for complete binary API verification.

Evidence: artifacts/core-refactoring/module-terminal-verification.json and
.testagent/module-terminal-review.md. Review records R1-R5, 97 assertion sites,
the pre-fix failures, the initial baseline failure and final passing runs.
No measured coverage, mutation, performance or fresh package-feed claim.

Next: retry terminal completion and cleanup (including ignored faulted attempts),
then outer request/reply bridge ownership of failure tasks and remaining cleanup.
Continue the thirteen presentation boundaries, full activation/port/resource and
authoring/revision convergence, feature/performance matrix, coherent isolated
local package candidate and API gate improvements. The full goal remains active;
nothing was published or externally coordinated.

## Previous checkpoint: timer scheduling and canonical cleanup (2026-09-09)

Timer scheduling throw/false now faults remaining exchanges and closes admission;
settled replies/timeouts keep their outcomes. Callback failures are contained and
reported on first timer disposal. One private drain replaces duplicated pending
collection/projection; no new runtime owner, public API or dependency was added.

Canonical application shutdown continues after exchange cleanup errors. Internal
faults still cancel routing before held native reservations release, and preserve
initiating/cleanup failures. Disposal attempts remaining owned cleanup without
disposing borrowed instances. The request/reply consumer's unchanged production
code is verified to cancel and join its blocked senders on scheduling failure.

Final full Release build: 66.24 seconds, zero warnings/errors. Selected regression:
3,761 passed in 79 Completed reports, including 213 release checks. Separate local
workflow acceptance: one passed. Focused coordinator: 41 passed; final Core suite:
110 per target. Eight new test methods and one expanded theory provide 32 added
executions; all 36 executions of the nine touched methods passed. Discovery grew
from 3,939 to 3,971 name tokens. Seventeen qualification hashes unchanged; scoped
diff check passed. Public API baseline, projects and dependencies unchanged.

Evidence: artifacts/core-refactoring/pending-scheduling-verification.json and
.testagent/pending-scheduling-review.md. The review maps R1-R10, records 115
assertion sites and static gap analysis, and distinguishes pre-fix failures and
one corrected narrow compile error from final qualification. All final processes
exited zero. No measured coverage, mutation, performance or package-feed claim.

Next: module trigger Stop-before-cleanup, correlated tracker terminal callback
delivery/cleanup, and retry stopping-token disposal. These were rechecked in
current source and remain open. Thirteen presentation boundaries and the full
activation/port/resource, authoring/revision, feature/performance and fresh
isolated package candidate gates remain active. No publication, external consumer
coordination or frozen release artifact changes.

## Previous checkpoint: pending exchange shutdown settlement (2026-09-09)

PendingExchangeCoordinator now completes and clears accepted exchanges before
fallible timer disarm. DisposeAsync still attempts timer disposal after Stop
failure and preserves either original exception or both in causal order. No
public abstraction, runtime owner, dependency or version change was introduced.

The regression tests reproduced five failures before the fix. Final full Release
build: 53.27 seconds, zero warnings/errors. Selected regression: 3,729 passed in
79 Completed reports, including 213 release checks. Separate workflow acceptance:
one passed. Focused coordinator suite: 27 passed. Four new test methods/seven
executions were discovered and passed. Eight qualification hashes were unchanged;
scoped diff check passed. All processes exited; no still-running run was restarted.

Evidence: artifacts/core-refactoring/pending-shutdown-verification.json and
.testagent/pending-shutdown-review.md. The latter maps R1-R5 to exact test names
and records assertion/static-gap review, including limits. No measured coverage,
executed mutation, performance or complete local-package qualification claim.

End-to-end shutdown safety remains unfinished: individual timer scheduling paths
can still strand tasks, and callers that invoke Stop before their own cleanup
can still exit early. Current-source locations and proposed follow-through are
recorded in the review. Address canonical Core and its consumers next. Thirteen
module presentation boundaries and the broader activation/port/resource,
authoring/revision, feature/performance and fresh package-candidate gates remain
open. The full approved goal is active; nothing was published or coordinated
with an external consumer.

## Previous checkpoint: routing presentation made optional (2026-09-09)

Routing.Composition retains its window, correlation and join structural contracts
without Designer. Optional Routing.Designer supplies AddRoutingDesign over the
existing descriptor projection, preserving selector hints, nullable delegate type
descriptions, requiredResources, defaults/bounds and join-port ordering. Runtime
factory bodies are identical; no node algorithm, creator or runtime owner added.

Full non-incremental Release build: 66.91 seconds, zero warnings/errors. Selected
regression: 3,722 passed across 79 completed reports, including 213 release checks.
Workflow acceptance: one passed. Focused composition/runtime/release: 17/54/82
passed. Five new methods/executions discovered and passing. All final processes
exited zero. All 29 qualification hashes match; scoped diff check passed. Two
archives contain both targets and exact current dependency groups, but do not
establish a coherent feed or isolated package-only consumer acceptance.

API row 22 (66 declarations, three contract initializer changes) and new row 67
(two declarations) individually reviewed; other existing rows unchanged. Initial
preflight API failure is recorded separately from final qualification. Evidence:
artifacts/core-refactoring/routing-design-verification.json and
.testagent/routing-design-review.md. Four new substantive tests have 33 direct
assertion sites; the release wrapper uses the four-site shared boundary helper.

Thirteen ordinary module presentation dependencies remain. Before continuing
those, address the revalidated Core shutdown gap: PendingExchangeCoordinator.Stop
can set stopped then fail in timer.Change before settling entries, and DisposeAsync
calls Stop before timer disposal. Current source confirms it is still open; no
coordinator change was made in this slice. Broader activation/port/resource and
authoring/runtime convergence, performance acceptance and a fresh isolated package
candidate remain open. Goal active; no publication or external coordination.

## Previous checkpoint: HTTP, mapping and validation presentation made optional (2026-09-09)

Three more module registration packages now retain their structural descriptors
and portable contracts without Designer. New optional family Designer packages
provide AddHttpDesign, AddMappingDesign and AddValidationDesign over the existing
descriptor projection. Factories, node algorithms, processing profiles, canonical
shapes and host resource ownership remain unchanged. No new creator/runtime owner.

Full non-incremental Release build: 84.98 seconds, zero warnings/errors. Selected
regression: 3,717 passed across 79 completed reports, including 212 release checks.
Workflow acceptance: one passed. Focused HTTP/mapping/validation: 17/16/15 passed;
focused release: 81 passed. Twelve new methods/executions discovered and passing.
All final commands exited zero. All 45 qualification hashes match. Six archives
contain both targets and exact current dependency groups; not a coherent feed or
isolated package-only consumer result. API rows 16/24/34 individually reviewed and
new rows 64/65/66 added, with other existing rows unchanged.

Evidence: artifacts/core-refactoring/stateless-design-verification.json and
.testagent/stateless-design-review.md. The review records requirement mapping,
71 direct assertion sites, four shared boundary-helper sites and static
pseudo-mutation analysis. Initial three release documentation/baseline failures
are recorded separately from final success. No measured coverage/performance claim.

Fourteen ordinary module presentation dependencies remain. Continue the approved
family migration, including routing, while keeping component-local activation and
authoring/runtime convergence in scope. Full performance acceptance and a fresh
isolated package candidate remain open. Overall goal active; no publication or
external coordination.

## Previous checkpoint: messaging presentation made optional (2026-09-09)

The messaging Composition package retains four runtime contracts, portable
component/resource authoring and resource lifetimes without a Designer dependency.
Optional Mqtt.Designer owns presentation hints and the moved event descriptions.
AddMqttDesign decorates registered structural facts without resolving clients or
creating components. No new creator or runtime owner. Constructors, transport
algorithms and production target frameworks are unchanged.

The migration exposed premature validation of enum metadata in the shared
descriptor projection. A new regression reproduced it; validation now applies to
the completed decoration, before structural checks and registration. Invalid final
choices remain rejected. Existing release checks now recognize runtime-only
contract constructors while retaining all option/default/bounds assertions.

Final Release build: 69.09 seconds, zero warnings/errors. Selected regression:
3,705 passed across 79 completed reports, including 209 release checks. Workflow
acceptance: one passed. Focused messaging/Designer/release: 40/148/78 passed.
Discovery confirms six new methods/executions; all final processes exited zero.
All 27 qualification hashes match. Two archives were inspected for both targets
and dependency direction. Source API row 12 was individually reviewed and row 63
adds the optional package; other existing rows unchanged.

Evidence: artifacts/core-refactoring/messaging-design-verification.json and
.testagent/messaging-design-review.md. Earlier failed attempts and corrections are
separate from final qualification. No measured coverage or performance claim.

Seventeen ordinary module registration dependencies remain. HTTP is next, followed
by the remaining stateless/stateful families. Full port/resource/activation,
authoring/routing convergence, performance matrix and fresh isolated package-only
consumer validation remain open. These two archives do not establish release
readiness. Overall goal active; no publication or external coordination.

## Previous checkpoint: serialization presentation made optional (2026-09-09)

Serialization runtime registration no longer depends on Designer. Its six existing
contracts retain canonical ports, input shapes, options and borrowed clock behavior.
The optional Serialization.Designer package retains all visual hints through
AddSerializationDesign. Generic AddDesignMetadata decorates existing descriptors
without constructing components or changing their structural contracts. It shares
the existing metadata projection helpers; no additional creator or runtime owner.
Both production target frameworks remain supported.

Final Release build: 40.30 seconds, zero warnings/errors. Selected regression:
3,699 passed across 79 completed reports, including 208 release checks. Workflow
acceptance: one passed. Focused Designer/Serialization/release: 22/16/37 passed.
Discovery confirms six new methods/twelve executions; all final processes exited
zero. Rechecked all 26 qualification hashes. Final package archives contain both
target assemblies, and the runtime archive has no Designer dependency. Only the
reviewed API row 43 changes; the optional package adds row 62.

Evidence: artifacts/core-refactoring/serialization-design-verification.json and
.testagent/serialization-design-review.md. The review records 54 direct assertions,
static pseudo-mutation reasoning, and earlier failed attempts separately from the
passing qualification. No measured coverage or performance claim.

One of 19 ordinary module registration dependencies has been removed; 18 remain.
Full module/resource/activation/authoring and routing convergence, performance
acceptance and a fresh coherent isolated local package candidate are still open.
These two package archives do not establish release readiness. Overall goal active;
no publication, external coordination or frozen historical artifact changes.

## Previous checkpoint: registration port declarations consolidated (2026-09-09)

The optional registration builder now keeps one port-declaration collection instead
of four independently mutated collections. Metadata views, binding identities and
activation bindings are derived from one frozen snapshot. Copying declarations
preserves independent edits. Name validation no longer needs a metadata-type switch.
Both existing factory modes retain static schema inspection without construction;
no new production type, public API or factory was introduced.

Full Release build: 66.64 seconds, zero warnings/errors. Selected regression:
3,687 passed in 79 completed reports, including 207 release checks. Local acceptance:
one passed. Final focused registration/Designer checks: 40/20 passed. Ten repeated
runs of the six added cases: 60 passed. Discovery identifies all six cases. Source,
test and unchanged API-baseline hashes were rechecked after verification.
Evidence: artifacts/core-refactoring/port-declarations-verification.json and
.testagent/port-declarations-review.md. The manifest distinguishes completed report
evidence from process responses unavailable after context recovery.

This is a verified internal simplification, not completion of the full migration.
Component-local port/resource/activation/module and authoring convergence, routing
consolidation, performance acceptance and a fresh isolated local package candidate
remain open. The overall goal remains active. No publication or external coordination.

## Previous checkpoint: secondary composition runtime removed (2026-09-09)

Deleted ApplicationRuntime, ApplicationRuntimeComponent and RuntimeNodeKey, with
no replacement compatibility runtime. Production revision execution already uses
Core lifetime directly. The remaining direct-runtime tests were migrated: shared
lifecycle behavior now exercises Core on both frameworks, native component port
and cleanup behavior stays in Composition, and the public FlowApplication handles
the real held-work terminal-control drain scenario. New guards verify failed event
attachment releases its target without taking the caller's node, and all three
retired runtime types are absent from the actual composition assembly.

Full Release build: 64.39 seconds, zero warnings/errors. Selected regression:
3,681 passed in 79 completed reports including 207 release contracts. Acceptance:
one passed. Ten repeated runs: 210 passed in 40 reports. Discovery confirms all
21 selected executions. Only API baseline row 5 changes: 15 removed declarations,
individually reviewed. Updated current documentation/package notes; historical
comparison artifacts are not presented as a new qualified package candidate.
Evidence: artifacts/core-refactoring/runtime-removal-verification.json and
.testagent/runtime-removal-review.md. No publication or external coordination.

The overall goal is still active. Single port metadata/resource/activation/module
and authoring convergence, full routing consolidation, performance acceptance and
a fresh isolated local package candidate remain open. Removal of the second public
runtime is not proof that the complete framework refactoring is finished.

## Previous checkpoint: direct revision component ownership (2026-09-09)

The revision host no longer creates a composition runtime just to own components
and forward activity, or invents synthetic component names for that purpose. Its
existing candidate owns actual component instances and uses Core FlowLifetime for
startup, first-failure coordination and joined completion. Drain plans depend on
completion rather than a second runtime. Terminal-control completion is shared.

ComponentInstance owns bounded legacy event observation without requiring any
application host. The existing revision event attachment publishes both legacy
and named component events to application events and runtime activity. The extra
runtime broadcast/unbounded AttachActivity forwarding hop is gone from revision
execution. A source scan finds no ApplicationRuntime or AttachActivity references
in Engine C# files. No new runtime, creator, scheduler or package was added.

Full non-incremental Release build: 63.47 seconds, zero warnings/errors. Selected
regression: 3,671 passed in 79 completed reports, including 207 release contracts.
Local acceptance: one passed. Ten repeated new-test runs: 80 passed in ten reports.
Discovery confirms six methods/eight cases/eight framework executions. Only source
API baseline row 10 changed after individual review of two removed declarations
on internal types. Evidence: artifacts/core-refactoring/revision-lifetime-verification.json
and .testagent/revision-lifetime-review.md. The manifest distinguishes complete TRX
evidence from the final regression process response lost during context recovery.

The older public composition runtime and its direct tests are still present and
must be migrated/deleted next; they are not an approved compatibility layer. Full
routing/authoring convergence, single port declaration, creator/resource/module
migration, performance acceptance and a fresh isolated local-package candidate
remain open. No publication or external coordination took place.

## Previous checkpoint: shared fanout launch and join (2026-09-09)

Core output and stable revision output now share the complete branch launch/join
operation, not only failure joining. Duplicate launch loops were removed. A
synchronous route failure can no longer bypass cancellation and joining of already
started work; throwing cancellation callbacks do not replace the original failure.
Zero/single-route fast paths remain. No public API, scheduler or dependency added.
Revision snapshots, capture and barriers retain their tested behavior.

Verified Release build: 70.60 seconds, zero warnings/errors. Non-external regression:
3,663 passed in 79 reports, including 207 release-contract tests. Local acceptance:
one passed. Ten repeated new-test runs: 170 passed in 30 reports. Discovery confirms
three methods/fourteen cases/seventeen framework executions. API baseline unchanged.
Evidence: artifacts/core-refactoring/fanout-kernel-verification.json and
.testagent/fanout-kernel-review.md. The initial regression experiment reproduced
three synchronous-failure cases; red/intermediate runs are not qualification.

This is partial convergence, not a release candidate. Stable port capture/barrier
ownership, route metadata and component activation still require consolidation.
The approved module/resource/creator migration, complete performance comparison
and fresh isolated local-package acceptance remain open. No publication or external
coordination took place.

## Previous checkpoint: native terminal controls (2026-09-09)

Actual trigger Ack/Nak ports are bounded native target blocks; the callback-only
target was removed. Source production completion is distinct from full control
completion. Core drains business routers before closing controls; optional runtime
and revision-host drain follow the same ordering. Only explicitly declared terminal
controls are excluded from business cycle dependencies. Canonical signal links keep
the original envelope. The legacy adapter's recursive overload was fixed with an
explicit typed dispatch and value/error regression cases.

Qualified Release build: 41.02 seconds, zero warnings/errors. Full non-external
regression: 3,646 passed in 79 reports, including 207 release-contract tests. Local
acceptance: one passed. Ten repeated new-test runs: 200 passed in 40 reports.
Discovery: nine methods, twelve cases, twenty framework executions. Only API rows
0, 5, 10, 11 and 43 were individually reviewed and updated. Twenty-eight source/test/
baseline hashes were recorded and rechecked unchanged. Evidence and limitations:
artifacts/core-refactoring/control-ports-verification.json and
.testagent/control-ports-review.md. Earlier failed/intermediate artifacts are retained
but are not qualification. No publish or external coordination.

The overall goal remains open. Next: converge the revision execution path on Core,
continue single port declaration/resource/module migration, then finish performance
acceptance and a fresh isolated local package candidate. This checkpoint does not
approve a permanent second runtime or arbitrary business feedback.

## Previous checkpoint: source shutdown consolidation (2026-09-09)

FlowSource now has one producer/output finalization path instead of separate paths
for normal production, no-start completion and disposal. All owned outputs share one
list. Disposal claims the same start state; late lifecycle calls cannot restart it.
Cancellation uses one cached platform task, so callback failures cannot escape the
stop method and bypass cleanup. Component completion joins production and callbacks,
retains original and callback errors, then joins outputs. Faults still release data
outputs immediately rather than waiting for slow callbacks. No public API, creator,
runtime, queue, package or dependency was added.

Behavior clarification: stopping is signaled immediately but callbacks execute
asynchronously; Completion joins them. Natural production end also signals stopping.
Detached work started by callbacks is not tracked. Repeated concurrent Dispose keeps
the existing first-caller behavior, not a new shared-await contract.

Qualified non-incremental Release build: 27.28 seconds, zero warnings/errors. Full
non-external suite: 3,625 passed in 79 reports, including 207 release-contract tests.
Local acceptance: one passed. Ten repeats: 340 passed. Solution discovery: nine new
methods/seventeen cases/thirty-four framework executions. API baseline unchanged.
Initial tests reproduced ten callback-related failures. A later held-callback test
caught an output-release ordering regression in the first refactor; both cases now
pass, and full validation was repeated after correction. Qualified-prefix artifacts
and two source hashes: artifacts/core-refactoring/source-shutdown-verification.json.
Full test/assertion/gap review: .testagent/source-shutdown-review.md.

Next remains actual trigger Ack/Nak control-port migration, not another broad review.
Keep control inputs accepting while downstream data drains; do not simply put them
in the business-input cycle detector or close them when source production stops.
Current direct execution waits producer router completion, but the retained update
drain waits whole component completion per stage and needs the same two-phase model.
Signal identity/outcome behavior and existing signal links must be preserved.
Trigger coordinator Stop cleanup with failing custom clocks remains an open risk.
The broader module/resource/metadata, optional-layer, performance and fresh local
package qualification gates remain open. No publication or external coordination;
context-aware Add and unapproved template/feedback scope remain deferred.

## Prior checkpoint: command processing runtime (2026-09-09)

MqttControlNode now derives the Core one-result FlowTransformNode and exposes its
actual ports for direct instance-first graphs. It is 105 lines (119 net lines
removed); private cancellation/completion/forwarding ownership is gone. Native
transform ordering and bounded serial output forwarding live in Core. FlowNode and
FlowTransformNode share one internal processing/output lifetime; ordinary action
error conversion, extra outputs and drain hooks remain intact. Expected command
failures remain data, incoming error envelopes remain the same reference, and
unexpected failures cancel peers before waiting for their exit. Controller calls
receive cancellation and disposal joins them without owning the shared controller.
Throwing cancellation callbacks cannot strand the other blocks' shutdown.

Final non-incremental Release build: 30.76 seconds, zero warnings/errors. Full
non-external suite: 3,591 passed in 79 reports (207 release-contract checks included).
Local acceptance: one passed. Ten fixture repeats: 250 passed. Discovery: ten new
methods, sixteen cases, twenty-five framework executions. API rows 0/11 were
reviewed individually; no blanket baseline acceptance. Evidence/hashes:
artifacts/core-refactoring/command-runtime-verification.json. Full requirement,
assertion and mutation review: .testagent/command-runtime-review.md.

Next: migrate trigger Ack/Nak from separate IFlowSignalTarget callbacks to actual
addressable control blocks, preserve signal outcome/identity behavior, and join
explicit acknowledgement calls during shutdown. Do not expose ports while leaving
their real lifetime behind the old callback path. Actual update/Core routing,
resource/metadata/module migration, optional designer/operations/telemetry,
performance matrix and coherent fresh package-only qualification remain open.
Concurrent repeated component Dispose calls retain the existing first-caller
semantics, not a new shared-await guarantee. No publication or external coordination.
Deferred context Add/template/feedback scope is unchanged; goal remains active.

## Prior checkpoint: trigger outcome cleanup (2026-09-09)

Closed the timeout-observer cleanup risk from the previous checkpoint. Disposal
joins held outcomes, attempts coordinator/timer and registration cleanup even when
earlier cleanup fails, preserves original single errors and aggregates all concurrent/
secondary failures. Unexpired pending work settles without advancing the clock.
Timeout fault publication now occurs inside the observed task, before it completes;
the remaining continuation only removes/observes it. No new runtime, creator,
public API, project or dependency was introduced.

Six initial executable regression cases failed before the fix. Final non-incremental
Release build: 20.27 seconds, zero warnings/errors. Full non-external suite: 3,566
passed in 79 reports; one local acceptance passed; 110 repeated fixture checks
passed. Discovery includes eleven new cases in four methods. API baseline unchanged.
Evidence and three C# hashes: artifacts/core-refactoring/trigger-cleanup-verification.json.
Requirement/assertion/mutation review: .testagent/trigger-cleanup-review.md.

Scope remains explicit: timeout observers are joined; direct Ack/Nak caller work is
not yet integrated into Core control-port lifetime. The ordered parallel command
node and actual update/Core executor still require consolidation. Custom clock
failures during coordinator Stop, resources/port metadata, remaining modules,
optional designer/operations/telemetry, performance and fresh package-only candidate
qualification remain open. No publication or external coordination. Deferred scope
is unchanged; the full goal remains active.

## Prior checkpoint: transport source lifetime (2026-09-09)

The client-event and subscription-trigger sources now inherit Core FlowSource.
Deleted their 147-line private source pump and repeated lifecycle/port forwarding.
Ordinary constructors produce actual Core components. Core joins active enumeration
before fault completion and subscription disposal; shared controllers stay borrowed.
Protocol acknowledgement, timeout and subscription behavior remains in the module.
No new creator, context, queue, package or dependency was introduced.

Verified: non-incremental Release build 29.82 seconds, zero warnings/errors; 3,555
non-external tests in 79 reports; one local workflow acceptance; 200 passes across
ten repetitions of twenty new lifecycle cases. Solution discovery finds all twenty
cases in ten methods. Manual API review changes only module row 11 (311 to 291
source declarations); dependent binaries must be rebuilt. Evidence and three C#
hashes: artifacts/core-refactoring/transport-source-verification.json. Requirement,
assertion and static mutation review: .testagent/transport-source-review.md.

This is source lifetime consolidation, not a fully migrated module. Ack/Nak still
use their signal API/update-host addressing, not direct Core graph control ports.
The command node still preserves its ordered parallel implementation; replacing it
with unordered emission would remove behavior. Trigger cleanup after a failing
outcome observation remains a separately recorded ownership risk. Resource/document
bindings and the actual update executor still require migration.

Overall goal active: remaining module/resource/port metadata work, optional designer/
operations/telemetry boundaries, performance qualification and coherent fresh local
package-only acceptance remain. No fresh restore, release or external coordination.
Context-aware Add and unapproved template/feedback remain deferred.

## Prior checkpoint: direct source components (2026-09-09)

GeneratedSourceNode<T> and SequenceSourceNode are directly Core components through
FlowSource. Removed the two private forwarding source objects and duplicate port/
lifecycle members. workflow.Add receives the actual instance; native ports and
same-message fanout are verified. GeneratedSourceNode.Create detaches JSON items
at construction and is shared by direct document and retained catalog bindings.

Optional ComponentBindings.AddSources maps documents to ordinary constructors with
explicit borrowed clocks. No new creator, activation context, executor or package.
Standalone name defaults remain; graph addresses are not injected as domain names.
Resource declarations and processing profiles still require migration. Diagnostic
Events remains a separate Core stream; existing named business-event routing is
retained on the update path. Existing designer/catalog/typed authoring still works;
the composition assembly's designer dependency has not yet been separated.

Verified: clean Release --no-incremental build 31.53 seconds, zero warnings/errors;
3,535 non-external tests in 79 reports; one local workflow acceptance; 250 repeated
passes of the 25 new cases. Manual API review changed only Sources rows 19/20 in
this slice. Source declaration count is reduced by removing forwarding members;
this is not a binary compatibility guarantee. Six C# file hashes and exact results:
artifacts/core-refactoring/source-components-verification.json. Requirement,
assertion and mutation review: .testagent/source-components-review.md.

Retained failed attempts include an internal helper access, test JSON missing the
required Resources object, an immutable-map ordering assumption and expected API
baseline drift. Parser/runtime contracts were not weakened for the tests.

Goal remains active. Actual update/Core execution convergence, resources/port
metadata, remaining modules, designer/operational/telemetry separation, complete
performance qualification and fresh package-only consumer acceptance remain open.
No fresh restore, publication or external coordination. Deferred scope unchanged.

## Prior checkpoint: ordinary Core hosting (2026-09-09)

Normal hosting now accepts the actual instance-built Core FlowApplication through
AddFluxFlow(application). Registration adds only that borrowed graph and a thin
host bridge: start the graph, observe its completion/failure, drain on host stop.
It does not construct definition sources, descriptors, revision snapshots, stable
port generations or rollback state. The caller retains graph disposal ownership.
Duplicate or mixed direct/update host registrations fail before mutating services.

The five definition/setup/configuration/source hosting overloads are explicitly
named AddFluxFlowUpdates. No compatibility aliases remain. Existing update behavior,
operational integrations and in-repository callers are preserved through that
opt-in entry point. Samples, test support, acceptance sources, benchmark caller and
current documentation have been migrated. No new package or component creator.

Verified after final changes:

- Clean non-incremental Release build: 18.33 seconds, zero warnings/errors.
- Full non-external suite: 3,510 passed in 79 reports, zero failures/skips.
- Local workflow acceptance: one passed.
- Ten hosting repetitions: 150 passed; fifteen new cases in eleven methods.
- Direct graph identity, absence of revision services, startup validation, source
  fanout identity, request/reply, drain/cancel/failure and caller ownership tested.
- Engine public API baseline row 10 manually reviewed; Core API unchanged.
- 27 touched C# files hash-recorded in core-host-verification.json under
  artifacts/core-refactoring. Review: .testagent/core-host-review.md.

Intermediate failures remain recorded: one omitted benchmark call, release-contract
expectations, and two corrected test assumptions about startup validation and
immediate-stop background scheduling. No runtime workaround was added for test timing.

Normal direct hosting is now independent of revisions, but the explicit update
host still owns its existing execution/routing graph. Moving that graph onto the
actual Core executor remains required before final release; this is not a second
approved permanent runtime. Activation/resource/port metadata ownership, remaining
module families, telemetry/operational ownership, complete performance qualification
and a coherent local package-only consumer run remain open. No fresh isolated-cache
restore, publication or external coordination. Context-aware Add and unapproved
template/feedback additions remain deferred.

## Prior checkpoint: portable delivery contract (2026-09-09)

One existing Core FlowDeliveryPolicy now survives JSON parsing, fluent LinkTo /
LinkFrom / Connect authoring, immutable declarations, export, compilation, direct
Core graph construction, native predicates, and editable document persistence.
Reliable remains the default and preserves compact unconditional port strings.
Explicit RejectWhenFull and IsolateRejection stay in the flat link object with
Condition and Parameters. Strict named values reject numeric, blank, unknown,
composite, wrong-case and duplicate inputs. Conflicting policies cannot silently
deduplicate an otherwise identical direct declaration or bypass authored-link checks.

Direct isolated links require an explicit host handler with source/target addresses,
the original envelope and failure. No callback is passed to other Core policies;
the existing hosted rejection stream remains its host's reporting boundary. No new
queue, subscription system, component creator, or runtime was introduced.

Verified after final edits:

- Clean Release non-incremental build: 20.48 seconds, zero warnings/errors.
- Full non-external unit suite: 3,495 passed in 79 reports, zero failures/skips.
- Local workflow acceptance: one passed.
- Ten complete Json suite repeats: 520 passed.
- Solution discovery: 45 new cases in 13 methods (39 portable + 6 editor).
- 140 direct assertion sites; mutation-oriented and assertion-quality review recorded.
- Public API baseline rows 1/5/43 reviewed manually. Core public API unchanged.
  Optional-argument signature changes are binary-breaking for the planned release.
- Scoped whitespace check passed. Fifteen changed production files are hash-recorded
  in artifacts/core-refactoring/portable-delivery-verification.json.

The first full run had two release API-shape expectations still describing the old
surface; both were updated to assert the new optional enum/default and immutable
property, then the entire suite was rerun successfully. Intermediate failed test
artifacts are retained. Review: .testagent/portable-delivery-review.md.

This is not actual hosted Core graph convergence. Hosted stable ports/revisions
still own a routing graph; sharing delivery metadata/helpers does not finish that
requirement. Normal startup without revision machinery, activation/resources,
single metadata ownership, remaining module families, operational/telemetry ownership,
full performance qualification, and a coherent local package-only consumer run remain
open. No fresh isolated-cache restore, NuGet publication, or external coordination
was performed. Context-aware Add and unapproved template/feedback work remain deferred.

## Prior checkpoint: hosted native delivery and revision retirement (2026-09-09)

Hosted business routes now use the stable input's native awaited admission by
default. Core and Engine share the small fanout failure join; every selected sibling
starts before failure observation, pending admissions are canceled/joined on failure,
and no per-link queue was added. Merged the two manual-link wrappers into one
cancellable link around the existing canonical/signal edge routes. Conditions are
selected before business sends, preserving the original condition failure.

The additive compiled-link Delivery/WithDelivery surface reuses FlowDeliveryPolicy.
Behavior is intentionally breaking: immediate reject-and-continue requires explicit
IsolateRejection; RejectWhenFull faults the output. Five existing isolation callers
were made explicit. Policy changes participate in revision identity without allowing
duplicate routes differing only by policy. Portable authoring projection is not done.

Output revision gates protect capture, selection and initiation, not waiting for
native input capacity. In-flight work retains its selected snapshot; subsequent
dispatch uses the next snapshot. A full-suite run exposed two normal-retirement
races where late system events reached draining inputs. Candidate drain now stops
its own current revision's system routes and joins selected deliveries before input
drain. It preserves ordinary business routes, external observers and newer revision
routes on a shared surface. This is not full hosted executable-graph convergence.

Final evidence: artifacts/core-refactoring/host-delivery-verification.json and
.testagent/host-delivery-review.md. Clean Release no-incremental/no-restore build:
18.22 seconds, zero warnings/errors. Full non-external unit run: 3,450 passed across
79 TRX files, zero failures/skips. Local workflow acceptance: one passed. Ten repeated
complete Engine runs: 262 each, 2,620 passed. Ten new methods/eighteen discovered cases,
131 direct assertion sites. The initial two-case saturation regression failed against
the old implementation; failed/intermediate integration evidence is retained.

Only API declaration rows 5 and 10 changed: Composition adds Delivery/WithDelivery;
Engine's net three source declarations are public members on internal route helpers,
not three new consumer APIs. Reviewed and patched only those baseline rows. Core's
public declarations remain unchanged. No fresh restore or performance claim.

Remaining: actual hosted Core graph and fail-fast whole-host lifetime convergence,
optional revision/authoring boundaries and portable policy configuration, component
activation/resource/module migration and metadata consolidation, operational/activity
ownership, full performance qualification and coherent isolated local package-only
candidate. The historical native latency gap remains unaccepted. Nothing was staged,
committed, published, or sent to another consumer task. The approved goal remains active.

## Earlier checkpoint: canonical output capture (2026-09-09)

The optional capture path now receives the original non-generic FlowMessage.
Removed its typed-envelope projection and generic resolver; durable capture no
longer reserializes an already canonical payload or stores a CLR-type registry.
Capture(address, contractName) is the normal declaration, with a thin output-handle
convenience. Custom options/type metadata remain available at FlowValue creation.
Capture preserves dynamic JSON, unknown fields, custom converter output and exact
wire shape. Store schema, keys, provider interfaces, capture-before-dispatch,
failure/cancellation/drain and telemetry behavior are unchanged. Migration guidance
is in docs/27-durable-output-capture.md; sample and package-consumer restart source
now use the new declaration. The latter still requires the later package-only gate.

Verification: clean nonincremental no-restore solution build, zero warnings/errors
(23.53 seconds); 3,432 non-external unit passes across 79 TRX, including all 207
release checks; one workflow acceptance pass. Ten Engine capture repetitions pass
320 tests; ten durable capture/registration/instrumentation repetitions pass 460.
Zero final failures/skips. Four new methods/eight cases are present in discovery;
test-review evidence includes exact reference, JSON ownership, custom serializer
and real DI assertions. The first full run found one stale sample serializer-count
assertion; it was corrected to assert address/contract-only output capture, then
the full matrix reran successfully. API rows 10/57 are reviewed breaking changes;
declaration counts remain unchanged. No fresh restore or performance claim.

Evidence: artifacts/core-refactoring/canonical-capture-verification.json and
.testagent/canonical-capture-review.md. Prior input-checkpoint API prose was also
corrected: four consumer overloads, four public members on internal types.

This is a completed capture migration, not completion of the approved refactor.
Hosted business routing/revisions still need the shared Core graph, followed by
remaining authoring/module/operational separation, full performance acceptance
and a coherent local package candidate. Goal active. No publication, external
consumer coordination, staging or commit.

## Previous checkpoint: awaitable stable input admission

Stable input SendAsync now awaits capacity on its existing bounded ActionBlock.
Canonical input, typed convenience calls and native typed signal edges share that
admission owner. Explicit TrySend remains immediate and is exposed through both
ApplicationPortRuntime and ApplicationPorts (address/string facade overloads).
No additional queue, admission envelope, capacity semaphore or revision epoch.
Drain includes accepted work and producers already waiting when it closes new
admission. Queue status excludes waiting producers. Complete releases waiting
producers separately from draining accepted work; abort cancels accepted dispatch.
Acceptance remains local ownership, not processing or durable acknowledgement.

The full matrix identified a dependent policy that must stay immediate: durable
input releases a full input lease for retry. Its contract now uses TrySend, and
the existing saturation test keeps its exact retry/no-dead-letter assertions
with a bounded wait. No durable schema, lease or acknowledgement change.

Final verification: clean nonincremental compilation using existing restored
assets in 20.21 seconds, zero warnings/errors; **3,424 non-external unit executions
passed** across 79 result files, including 242 Engine, 163 durable input and 207
release-policy checks. One workflow acceptance passed. Nine new methods / 22 cases
cover bounded admission, cancellation, complete/abort, fast drain, replacement,
runtime/facade APIs, typed/error identity and request deadlines during admission.
Ten focused repetitions passed 680 executions; ten retained durable saturation
repetitions passed. Final failures/skips: zero. Discovery includes all 22 new cases;
one immediate-send test was intentionally renamed. Concurrent discovery appended
a runner prefix to an existing name, not a removed test.

The initial full run was stopped after identifying the waiting durable consumer;
it is not counted as a final passing run. The initial restoring build was denied
access to user-level package configuration by the sandbox. Dependencies did not
change, so the final compilation used --no-restore; no fresh restore/package-feed
verification is claimed. Failed comparison/diagnostic evidence is retained.

Reviewed API baseline changes: Engine entry 10, 403 to 411 lexical declarations
(four consumer TrySend overloads and four internal-type members); durable input entry
54 remains 214 with a changed lexical async modifier. No Core API change. SendAsync
waiting instead of returning Full is intentionally behavior-breaking; migration
notes describe TrySend and the distinction between admission and processing.
Evidence: artifacts/core-refactoring/awaitable-input-verification.json and
.testagent/awaitable-input-review.md. All launched commands have finished.

This completes the awaitable-input prerequisite, not hosted business-routing
convergence. Business links still use separate immediate revision routing. Shared
output/capture/revision drain, portable delivery policy, ordinary startup without
revision preparation, module/authoring/operations migration, activity ownership,
the broad performance matrix and the coherent local package-only candidate remain
open. No new performance acceptance; the native latency gap remains unaccepted.
No publication, external consumer coordination, staging or commit. Goal active.

## Previous checkpoint: explicit Core route admission isolation

Core now represents the hosted routing behavior that reports a rejected message and
continues: FlowDeliveryPolicy.IsolateRejection. It uses the same FlowRouter selection
and FlowOutput native admission/fanout, with no new queue, retry runtime or creator.
LinkTo, LinkFrom and OtherwiseTo require a synchronous rejection handler only for
this explicit policy. Reliable remains the default, and RejectWhenFull still faults
the graph. Full targets stay linked for recovery; failures report the original
envelope/error. Condition failures can be isolated without hiding ordinary unmatched
messages. Fallback remains selection, not retry after rejected admission. Handler
exceptions cannot interrupt siblings; blocking/asynchronous handler work remains
the caller's responsibility. Component lifetime faults remain graph-wide.

Verification: clean nonincremental build in 40.71 seconds, zero warnings/errors;
**3,402 non-external unit executions passed**, including 220 Engine and 207 release
checks; one workflow acceptance passed. Focused graph tests: 114 passed on the two
frameworks combined. Ten repetitions: 1,140 passed. Nine new methods/48 executions
cover recovery, per-message reporting, condition/fallback behavior, strict controls,
symmetric validation and component lifetime. Test review strengthened selected-state
reset and added strict/fallback controls. No final failures/skips. Discovery contains
all 48 new cases and no missing previously recognized names; concurrent output joined
two existing names on one line, so physical line counts are not a test inventory.
All commands finished. Evidence: artifacts/core-refactoring/route-isolation-verification.json
and .testagent/route-isolation-review.md.

Reviewed only Core's source API baseline, 383 to 384 declarations. Adding the optional
handler changes compiled link signatures: consumers must rebuild, though existing
source call defaults remain unchanged. No compatibility shim or publication. The
pre-update API comparison failure is retained as expected review evidence.

This completes an explicit routing-policy prerequisite, not hosted business-routing
convergence. Stable awaitable input admission, capture/revision drain and routing
snapshots still need to converge on the shared kernel; portable policy wiring and
normal startup without revision preparation remain open. Module/authoring/operations,
activity attachment ownership, the broader performance matrix and coherent local
package-only candidate also remain open. No new performance acceptance and no change
to the unaccepted native latency gap. No publication, external consumer coordination,
staging, commit or unapproved capability. Goal remains active.

## Previous checkpoint: hosted signals share Core delivery

System events, diagnostics and activity now use the existing Core FlowOutput rather
than Engine's separate FlowFanoutSource. Removed that 430-line internal fanout source
and its manual pump/link machinery; preceding source is retained under
artifacts/core-refactoring/signal-fanout-before. The shared output exposes real
native source reservation callbacks. Internal isolation detaches broken signal
subscribers without interrupting healthy siblings; public Core outputs retain
strict reliable failure behavior. Immediate signal admission shares the same
capacity gate and dispatcher as asynchronous sends. No second queue, creator API,
context overload or new routing abstraction; Core has internal access for Engine
without an outward dependency.

System events still backpressure; diagnostics/activity still reject overflow
promptly. Capacity remains 256 waiting items plus one active fanout. Live subscriber
selection now happens at admission, matching Core: linking later does not replay
an older queued backlog. Predicates, MaxMessages, identity and source reservations
are verified. Graceful completion drains accepted items; disposal detaches all
streams before waiting, abandons queued messages and does not complete subscriber-
owned targets. Legitimate native reservations remain target-owned until released.

Verification: clean nonincremental solution build in 1m15.24s, zero warnings/errors;
**3,354 non-external unit executions passed**, including 220 Engine tests and all
207 release checks. One workflow acceptance and 420 repeated signal/activity/event
executions passed. Zero failures/skips. Solution discovery grew by13 and lists all
new cases. Six new methods/13 executions verify the behavior; an existing invalid
completion-test target was corrected to honor consumeToAccept without weakening its
failure-isolation assertion. Only Engine's lexical declaration baseline changed
(425 to403) because the checker includes public members of internal types; Core's
public baseline did not change. Exact evidence: signal-fanout-verification.json in
artifacts/core-refactoring and .testagent/signal-fanout-review.md. All commands finished.

This completes shared signal delivery, not hosted business routing convergence.
Business links still use immediate admission and separate revision routing;
ordinary startup still prepares revision state. Activity attachments retain their
preexisting forwarding queue/link-only ownership and need further operational
lifetime work. Module/authoring/operations migration, broader performance and a
coherent package-only candidate remain open. No new performance measurement or
acceptance of the prior native latency gap. No publication, external coordination,
staging, commit or unapproved capability. Goal remains active.

## Previous checkpoint: hosted input scheduling uses a bounded Dataflow block

ApplicationInputPortCore now uses one bounded serial ActionBlock instead of its
manual Queue, capacity semaphore, wake-up semaphore/Pulse, retained retry field
and background pump. The active delegate retains rejected work ahead of queued
messages while waiting for a replacement attachment. Native block capacity includes
active work. The pending count is status/drain metadata, not a second admission
gate. Stable generation, pause, in-flight handoff and terminal reporting remain
host policy shared by canonical inputs and typed signal edges. Core, public
construction APIs and package references did not change.

Existing immediate admission, reference order, retained-work replacement, stale
attachment retirement, terminal reporting and abort behavior are preserved.
Review added target-shutdown failure and positive-capacity regressions: native
capacity -1 must not silently enable unbounded buffering. Rejected work is not
retried ahead of its original position or cloned into another queue. Abort still
cannot forcibly stop arbitrary noncooperative target code.

Verification: clean nonincremental solution build, zero warnings/errors,
24.16 seconds; **3,341 non-external unit executions passed**, including all 207
release checks; one workflow acceptance passed. Ten repetitions of input, port
and runtime-concurrency fixtures passed 410 executions. Zero failures/skips.
Discovery explicitly lists all ten new cases. The original six characterization
cases passed on both schedulers; review added four shutdown/capacity executions.
No declaration-baseline edit was required. All commands finished. Exact commands
and counts: artifacts/core-refactoring/input-dispatch-verification.json; preceding
source: artifacts/core-refactoring/input-dispatch-before; detailed test/assertion
review: .testagent/input-dispatch-review.md.

This is a prerequisite to hosted routing convergence, not completion of that gate.
Hosted business links remain immediate; routing/fanout is still separate from
Core, and ordinary startup still prepares revision state. Move those paths onto
the shared graph while retaining explicit isolation, reliable admission, stable
addresses and update rollback/drain. Module/authoring/operations migration, broader
performance verification and coherent package-only acceptance remain open. No new
benchmark was run; the preceding native latency gap remains unaccepted. No commit,
publication, external consumer coordination or unapproved capability.

## Previous checkpoint: shared component startup, stop and failure coordination

Core FlowExecution and composition ApplicationRuntime now delegate their component
lifetime to one internal FlowLifetime. It starts sources sequentially, stops entry
nodes and sources with control inputs once, observes both node completion and
additional work, and propagates the first failure even if a sibling's Fault callback
throws. The helper owns no queue, routes, ports, services, authoring model, revision
transactions or disposal policy. Core retains its route-first fault ordering and
fan-in completion; composition retains existing event and owned-component cleanup.
The implementation is shared through explicit internal access, with no new public
construction API or outward Core project/package reference. Preceding sources are
retained under artifacts/core-refactoring/shared-lifetime-before.

Composition now reliably faults siblings after startup, component or completion
failure; an extra descriptor task cannot conceal the underlying node's failure.
Outstanding additional work still delays completion and a later independent error
does not replace the first. A failure during a held source start prevents later
sources from starting. Already-running noncooperative work must still finish;
the helper does not forcibly terminate arbitrary component tasks.

Verification: final clean nonincremental solution build, zero warnings/errors,
23.74 seconds; **3,331 non-external unit executions passed**, including all 207
release checks; one separate workflow acceptance passed. Ten repetitions of the
composition lifetime fixture passed 100 executions; ten repetitions of Core
execution/graph fixtures passed 720 across both target frameworks. All have zero
failures/skips. Discovery includes all eight new executions. The original six
executions failed before the implementation; review added interrupted-start and
cancellation cases and strengthened first-error assertions. The final build also
corrected two test-only nullable warnings. No declaration-baseline edit was needed.
Detailed review: .testagent/shared-lifetime-review.md. Exact commands/results:
artifacts/core-refactoring/shared-lifetime-verification.json. All commands finished.

This closes duplicated component-lifetime coordination, not the full single-runtime
gate. Hosted business routing is still immediate; stable proxies, normal revision
preparation, optional updates and broader ownership/disposal convergence remain.
Next work must move those paths onto the same graph without losing isolation,
reliable admission, revision drain or rollback. Module/authoring/operations migration,
the broader performance matrix and a coherent package-only candidate are also open.
The preceding native latency gap remains unaccepted; these lifetime edits have not
been benchmarked. No commit, publication, external consumer coordination or
unapproved capability was introduced.

## Previous checkpoint: native target admission in the shared dispatcher

Selected named graph routes now use native SendAsync (or immediate Post for
reject-when-full) within the existing FlowOutput dispatcher and fanout loop.
Their already-computed selection mask no longer needs a second filtered callback
bridge. Public source links retain the native filtering/source callback protocol.
No new queue, runtime, fanout implementation, public type or package dependency.
Only two FlowOutput methods changed in this slice; the preceding source is retained.

Verification: clean nonincremental solution build, zero warnings/errors,
40.30 seconds; **3,323 unit executions passed**, zero failed/skipped, including
all 207 release checks; one separate workflow acceptance passed. Ten repetitions
of the complete instance-first graph fixture passed all 660 executions. Discovery
includes the eight new executions on both target frameworks. The new theory
demonstrably failed before the edit and checks initial admission, exact ordered
message references and completion for standard/native sources and one/three
recipients. Native reservation, rejection, capacity, fallback and failure-drain
coverage remains green. The public declaration baseline did not change.
Detailed review: .testagent/native-route-admission-review.md.

The intermediate native regression was reproduced with two launches: 1.47 times
the baseline mean and 1.51 times the allocation. Sampled profiling plus source
inspection identified forced asynchronous sender activity. After the change, the
matched native comparison is 1.08 times the baseline mean, 0.51 times its allocation
and approximately equal completed thread-pool work items (29 per operation).
The remaining native mean-latency gap is **open, not accepted**. The four standard
cases measured lower means and allocation. A noisy unconditional fanout case was
repeated with two launches: reported mean ratio 0.64, allocation ratio 0.23. This
supports improvement but not a fixed speedup; both runs retain outlier caveats.
Raw measurements, profile caveats, before/current identities and source hashes are
in artifacts/core-refactoring/routing-comparison/native-admission-report.md and
native-admission-identity.json. No saturation, retained-heap or per-message
tail-latency claim; Dry/profile timings are not acceptance measurements. All
benchmark/build/test commands at this checkpoint have finished.

Continue hosted/Core routing and lifecycle consolidation while carrying the native
latency gap as an explicit performance acceptance item. Module/authoring/optional
operations migration, the broader performance matrix and a coherent local
package-only candidate still remain. No commit, publication, external consumer
coordination, context-aware Add overload or unapproved capability was introduced.

## Previous checkpoint: native-source fault drain and matched routing evidence

Closed the preceding isolated shutdown gate: graph failure no longer strands a
native source that is already gracefully completing with queued output. Routing
is faulted before components release reservations, and a fault-only built-in
discard link lets native completion settle without another queue or receive loop.
The first graph failure remains authoritative; owned and borrowed disposal are
checked. Discard is failed work, not processing or acknowledgement.

Verification: clean nonincremental build, zero warnings/errors, 58.04 seconds;
**3,315 unit executions passed**, zero failed/skipped; one separate workflow
acceptance passed; all 207 release checks passed. The new two-case theory passed
40 repeated executions across both target frameworks. Discovery includes all four
new target-framework executions. Detailed review is in
.testagent/native-drain-review.md; evidence is under
artifacts/core-refactoring/native-drain-*. Only reviewed source-baseline row 0
changed; declaration count remains 383. The checker includes non-public types'
public members and syntax differences, not only external APIs.

Matched routing measurements now cover five before/current cases using the saved
pre-consolidation routing sources and verified separate binaries. Standard cases
allocated less and had lower measured mean latency. The native condition case
regressed from 37.416 to 57.941 us and from 6,357 to 9,554 bytes/operation. This
regression is **open, not accepted**; the current standard-condition run was also
multimodal. Methodology, confidence margins, exact result paths and binary/source
hashes are in artifacts/core-refactoring/routing-comparison/report.md and
identity.json. Dry validation results are not measurements. No saturation,
retained-state, telemetry, broad payload or per-message tail-latency claim.

Next: repeat and attribute the native regression. Source inspection identifies
the shared offer bridge's forced asynchronous sender path as a concrete lead,
not a profiled attribution. Preserve full native protocol and shutdown semantics
when optimizing; do not reintroduce a parallel fanout implementation. Hosted
routing/lifetime consolidation, module/authoring/optional operations migration,
the remaining performance matrix and coherent local package-only acceptance are
still required. No commit, publication or external consumer coordination.

## Previous checkpoint: direct graph routing shares component outputs

FlowRouter now selects routes without owning an ActionBlock, per-message selected
array or second fanout loop. Standard graph routes use the component output's
existing dispatcher and completion. Custom native sources link to a bounded
adapter of that same FlowOutput implementation. Native filters and explicit Post
admission preserve the reliable/immediate policy distinction. FlowExecution still
coordinates graph lifetime and fan-in. Hosted fanout/revisions remain separate.

Intentional backpressure change: standard component output capacity is authoritative;
linkCapacity bounds only a native-source adapter, including active delivery. No
public message type, cloning, conversion queue or creator API was added. The Core
README documents the bound and remaining shared-kernel scope.

Verification: clean nonincremental solution build, zero warnings/errors, 1m21.68s;
**3,311 unit executions passed**, zero failed/skipped; one workflow acceptance
passed; all 207 release checks passed. Ten repetitions of the instance-first graph
fixture passed all 540 executions. Solution discovery includes all eighteen new
cases (eight methods, run on both target frameworks). Evidence is retained under
artifacts/core-refactoring/routing-kernel-*. Detailed traceability and honest static
mutation limits are in .testagent/routing-kernel-review.md. Reviewed source-baseline
row 0 changed from 379 to 383 lexical declarations; this includes members of
non-public types, not four new consumer APIs. Previous baseline retained.

Next shutdown gate: isolate graph-initiated failure while an already-completing
native source retains buffered output. An early fixture's post-Complete source
Fault did not settle the graph. A native read-only probe confirmed that such a
Fault does not replace the buffer's pending graceful completion; draining completes
it successfully. The final capacity test drains normally and does not close the
graph-failure scenario. An initially scheduling-dependent reservation test was
synchronized on actual reservation acquisition before the final full rerun.

No new performance measurement, package-only acceptance, publication, commit or
external coordination. Whole-framework simplification remains incomplete.

## Previous checkpoint: hosted output dispatch uses its native block

ApplicationOutputPort now uses one bounded serial ActionBlock in place of its
BufferBlock/manual receive loop. An ordered private barrier in that same queue
replaces yield-polling drain. There is no new public message type, conversion
stage or second queue. Capture precedes delivery; revision gates still pause
dispatch. Fault/abort settle pending drains, detached sources retain in-flight
receive/observation availability, and a capture that ignores cancellation cannot
emit after abort. Public abort notification remains immediate, not a guarantee
that non-cooperative external work has exited.

Intentional contract change: hosted OutputCapacity includes active dispatch and
queued work. Transient drain barriers share the bound and pending-work status;
they never enter data or activity streams. Drain covers the accepted prefix;
whole-revision shutdown drains producers first. Standalone output capacity and
host/direct fanout policies still differ and remain an explicit migration gate.
This removes a manual pump; it does not complete shared-kernel consolidation.

Verification: clean nonincremental solution build in 1m57.94s, zero warnings or
errors; **3,275 unit executions passed**, zero failed/skipped; one separate
workflow acceptance passed. All 207 release checks passed, including the
unchanged public API baseline. Solution discovery includes all eleven new cases.
Artifacts: output-dispatch-clean-build.log, output-dispatch-unit-results,
output-dispatch-acceptance-results and output-dispatch-discovery.log beneath
artifacts/core-refactoring. Focused dispatch/capture suite: 19 passed. Initial
eight-case regression run: five failed against the old implementation, three
preservation cases passed. Ten additional dispatcher repetitions passed all
110 executions, zero failed/skipped (output-dispatch-repeat-results).
Detailed assertions and mutation-reasoning limits:
.testagent/output-dispatch-review.md. No new performance or release-readiness
claim, package publication, commit or external coordination.

## Previous checkpoint: portable documents and explicit authoring setup

The portable model/parser/condition source is physically owned by Json, using
FluxFlow.Json.Model and FluxFlow.Json.Links. Json references Core and Mapping;
Composition references Json, not the reverse. ApplicationDefinition is data-only.
ApplicationSetup retains native links and exact activation/resource contracts;
builder, compiler, host, source and revision consumers were migrated explicitly.
CurrentSetup exposes the full active setup; CurrentDefinition exposes its data.
The portable build extension now belongs to Composition.Authoring. Basic Fluent
still references only Core.

Checked export projects declarative links and rejects native predicates or live
activation contracts. Direct construction still supports native predicates.
Drain ordering now uses the compiled link graph, including JSON declarations.
No implicit conversions, forwarding metadata properties or compatibility aliases
were added. Resource execution on the direct document path and the remaining
activation/editor split are still incomplete.

Verification: clean nonincremental build, zero warnings/errors; **3,264 unit
executions passed**, zero failed/skipped; one separate workflow acceptance passed;
solution discovery completed. Evidence: portable-boundary-clean-build.log,
portable-boundary-final-unit-results, portable-boundary-acceptance-results and
portable-boundary-discovery.log beneath artifacts/core-refactoring. The preceding
full run had one outdated MQTT setup/document identity assertion; it now checks
both explicit surfaces and the entire matrix was rerun. The release suite's 207
checks pass, including six dependency release waves and executable samples.

Reviewed source-baseline rows: Json 1 (11 -> 57), Composition 5 (516 -> 476),
Engine 10 (421 -> 425). The complete prior baseline is retained in
public-api-before-portable-boundary.txt. These are intentionally breaking source/
assembly changes, not binary-compatibility proof. New regression traceability and
static analysis limitations are recorded in .testagent/portable-boundary-review.md.
No new performance claim, local package acceptance, commit or publication is made.

## Scope decisions

- Execute the approved plan and implementation prompt; preserve all approved existing capabilities.
- Direct component instances and component-owned creation methods are the ordinary construction path.
- The context-aware Add overload is deferred, not a prototype or acceptance requirement.
- New template-adapter and feedback-iteration capabilities remain outside approved scope. Existing cycle validation stays in place.
- No publication, tags, pushes, separate-repository edits, or external consumer coordination without separate authorization.

## Initial phase status (historical; current checkpoints above)

| Phase | Status | Evidence / next gate |
|---|---|---|
| 0: baseline and migration inventory | Baseline captured; broader workload gates tracked | Build, unit/acceptance, 62-package destination map and 60-component inventory captured; 18 short and 18 default benchmark cases measured; slow-target/cancellation/evaluator/telemetry workloads remain required before final acceptance |
| 1: instance-first vertical slice | Executable API checkpoint demonstrated | 25 new Core/document cases discovered and passed in the full unit filter; actual path and remaining deletion gates documented |
| 2: canonical runtime | Partial; direct/native routing shares the component output; hosted manual pump removed | Core owns foundation/coordinator and shared exchanges; native-source drain-failure isolation and performance comparison are next; direct/host routing and ordinary/revision lifetime duplication remain |
| 3: authoring separation | Partial; portable model/parser physically separated; setup and direct Fluent closure verified | Activation/resource bindings, portable authoring package disposition and editor dependencies remain |
| 4: all component families | Partial; production transport migrated | Runtime consumers, samples and fixtures use canonical bases; every family still needs a verified metadata/registration/lifetime disposition |
| 5: operational capabilities | Partial; selected host/durability boundaries verified | Native observation and canonical receive are verified; durable-input wire representation preserved; updates, capture, providers, telemetry and wider operational acceptance remain |
| 6: candidate verification | Pending | Full suites, performance comparison, local package-only consumer |

## Baseline evidence

Captured before production changes in `artifacts/core-refactoring/baseline-start`:

- Base commit: `4d11bd889679aa40970be18f8d4e0772732f2137`; existing uncommitted work is part of the baseline, not discarded.
- SDK: 10.0.400, resolved through the repository SDK policy.
- Inventory: 62 source projects, 148 total projects, 62 package-manifest entries; 315 changed/untracked files hashed.
- `working-tree.patch`, `index.patch`, and `changed-file-hashes.json` preserve diagnostic evidence of the starting changes. They are not a full backup of untracked file contents.
- `projects.json` is a project-XML inventory, not an evaluated build dependency graph. Component declaration discovery is lexical, not a complete public API manifest.
- Fresh Release solution build: passed, zero warnings and errors (`build-authorized.log`). The preceding sandbox attempt failed reading the user package configuration before compilation (`build.log`); it is not a source-code failure.
- Fresh unit filter: 2,947 executed, all passed, zero skipped/failed (`unit-results/*.trx`). This excludes acceptance, container, external smoke and licensed database integrations according to the repository filter.
- Separate workflow acceptance: one executed and passed (`acceptance-results/*.trx`).
- Explicit destination decisions for all 62 current source packages: `eng/refactoring/package-destinations.json`; the expanded artifact `migration-inventory.json` includes source paths, framework targets, references, transitive consumers, tests and samples. `migration-inventory.md` is the human-readable table. Every migration status remains pending.
- `public-components.json`: 60 public concrete components from all 62 Release package assemblies, with constructors, inherited public block ports and target packages. Reflection did not invoke constructors, registration methods or property getters. Family-specific dynamic ports/signals still need verification.
- Measurements: all 18 Short cases and all 18 Default cases completed. Canonical delivery covers 1/8/32 hops and fanout 1/2/8; existing request/reply and link compilation cases are retained. The Default measurement run used already-built pre-refactoring binaries; the record change was rebuilt only after that run finished. Full JSON exports preserve raw measurements.
- Container prerequisite probe could not find the engine pipe. A repeat with normal configuration access confirmed the configured desktop Linux engine pipe is absent. No container or external-service integration success is claimed, and licensed database tests were not run. This limits integration verification, not independent core implementation.
- No new application runtime, performance improvement, migrated module, or release-readiness claim has been established yet.

The capture helper refuses to overwrite an existing output directory. The earlier partial `baseline-initial` directory is retained; `baseline-start` is the successful inventory capture.

## Value contract decision

FlowValue becomes a sealed immutable record over its already-owned JsonElement. The existing ingress ownership copies remain. Record copies share that immutable storage; equality follows storage identity rather than structural JSON equality. Ordinary forwarding must still preserve exact message/value/header references. This is the first foundation change, not completion of the instance-first application slice.

The scoped test plan is `.testagent/core-refactoring-contract-tests.md`. No context-aware construction API has been added.

Verified foundation checkpoint:

- Nodes suite: 111 passed, including six new cases; zero failures/skips.
- Solution-level discovery includes all six new cases.
- Public declaration baseline: reviewed change only at package index 0 (Nodes), declaration count unchanged at 279. The old hash is retained in the baseline artifact. No unrelated package baseline was accepted.
- Full nonincremental Release build: passed with zero errors and eight warnings (four unchanged event-helper nullable warnings per target framework). The initial incremental baseline build reported zero warnings because those outputs were already present; it was not proof of a warning-free cold compile.
- First full unit rerun: 2,953 executed, 2,952 passed; one stale convention still required eight benchmark cases. Updated that convention to validate all eighteen cases, exact workload parameters, lifecycle setup and scoped discovery. Six focused release checks then passed. The initial failure remains recorded in its log.
- Final checkpoint build: nonincremental Release solution build passed, zero errors and the same eight event-helper warnings (`value-contract-final-build.log`). Final unit rerun: **2,953 executed and passed, zero failed/skipped**, including all 205 release checks (`value-contract-final-unit-results/*.trx`). Separate workflow acceptance: one passed (`value-contract-acceptance-results/*.trx`). These are checkpoint results, not proof of the unfinished application refactor.
- Refreshed static source pairing confirms the new fanout tests reference both FlowValue and FlowOutput. As before, pairing does not measure line or branch coverage.

The subsequent instance-first slice is now implemented; the earlier value-contract evidence above predates it. See the [API checkpoint](2026-09-08-core-api-checkpoint.md) for the actual execution path, compiled examples and the explicit temporary-dependency deletion gates.

## Instance-first slice

Implemented a small component-local FlowPorts contract, named workflow/component/port views, direct Core graph ownership and validation, native bounded routing blocks, fan-in completion, explicit fallback/discard, expected-route request/reply using the existing coordinator, and optional document/portable-fluent entry points. Direct construction does not instantiate the old engine, descriptors, service provider or revision machinery.

Lifecycle tests exposed and fixed premature completion on forced faults in the canonical node/source bases: disposal now waits for active processing/production to exit. Stopping an unstarted source completes it without running producer code. Startup failure stops all attached components, disposes owned instances and preserves borrowed disposal ownership.

Checkpoint evidence beneath `artifacts/core-refactoring`:

- `instance-first-lifecycle-results`: 18 Core cases passed.
- `instance-first-lifecycle-nodes-results`: 111 existing foundation cases passed after the lifecycle correction.
- `document-kernel-parity-final-results`: seven focused document/parity/export cases passed before the final export-projection assertion refinement; the full solution build includes that refinement.
- `instance-first-full-build.log`: nonincremental Release solution build passed, zero errors and the same eight event-helper warnings.
- `instance-first-full-unit-results`: 2,978 executed, 2,976 passed, two failures, zero skipped. All 25 new tests passed. Failures were the expected source-declaration baseline change and the unfinished package-reference train.
- Reviewed baseline update changed only Nodes index 0 (279 to 287 declarations) and Fluent index 52 (32 to 34). The preceding snapshot is `public-api-before-instance-first.txt`; the focused baseline check passed after acceptance. Core/Json are not yet manifested and therefore not covered by this package baseline.
- The remaining package convention reports Fluent's reference to the not-yet-manifested Json project. This is an actual incomplete migration gate, not a runtime-test failure. It remains enabled and must be resolved before a package candidate is produced.
- `instance-first-discovery.log`: solution-level discovery lists all 18 Core and seven Json tests, including the strengthened export-projection case.
- `instance-first-acceptance-results`: the existing workflow acceptance case passed.
- A broad diff whitespace check reports pre-existing extra blank lines in untouched memory files. They were preserved; they are not refactoring changes.

At the API checkpoint no package migration row was complete, and Core/Json were intentionally non-packable. The following project-consolidation step supersedes that temporary packaging state. The previous baseline artifacts remain unchanged.

## Foundation project consolidation

- Moved 33 source/readme files into Core: canonical and still-to-be-consolidated typed primitives under Nodes, owned data under Data, and the existing keyed coordinator under Coordination. Implementation namespaces are retained.
- A validated mechanical rewrite updated 86 project files, deduplicating references to the single Core project. No current project reference targets either retired project.
- Retired the two old package project files, their solution entries and the obsolete Nodes-only compatibility suppression. Source files were relocated rather than deleted. The removed tracked files remain recoverable through Git; published packages and historical artifacts were not changed.
- Core has no project dependencies; its only package reference is Dataflow. Json still references Core and Composition until portable model/parser migration is complete. The old Fluent execution path remains to be removed.
- The manifest now lists Core and Json in place of the retired package identities. Both use 1.0.0-alpha.1 as unpublished development versions. These are packable development projects, not a verified candidate, and nothing has been packed or published in this step.
- `core-project-consolidation-build.log`: fresh Release solution build passed, zero errors and the same eight event-helper warnings before their subsequent correction.
- `core-project-consolidation-unit-results`: 2,978 executed, 2,927 passed, 51 failed, zero skipped. All failures were release fixtures tied to the retired alias/path, the historical ledger's old package identity, or the not-yet-updated public declaration baseline. Runtime/component/authoring suites passed. Keep this failure evidence; fixture migration is in progress.
- Release fixtures now use the real Core package, while noninitial binary-baseline tests use the existing Mapping package. Initial-release and existing-baseline policies remain independently tested. The July cleanup ledger remains historical evidence; its test recognizes the later foundation ownership move rather than rewriting the old version.
- Added missing successful-outcome nullability annotations to event projections and made the retained typed helper return false for error/null payloads. Two regression cases cover those failure paths; no warning suppression was added.

Project/source ownership consolidation is not complete runtime consolidation. Generic and canonical processing classes, the temporary routing queue, the output source facade, and old application/authoring paths remain deletion gates. No feature family is marked fully migrated on the strength of this project move alone.

### Verified native output dispatch checkpoint

- Replaced the manual output queue, available-item semaphore and pump with one serial ActionBlock. The queued-slot gate retains the existing capacity contract (waiting items plus one active fanout) and acceptance-time link snapshots.
- Two new regressions first failed on the previous implementation: immediate rejection/throw prevented later healthy sibling offers. Dispatch now starts every sibling offer before failure observation, while still cancelling postponed deliveries and retaining the initiating exception.
- Foundation suite: 115 passed. Full nonincremental Release solution build: zero warnings/errors (`native-dispatch-full-build.log`). Full non-external unit filter: **2,982 executed and passed**, zero failures/skips, including all 205 release checks (`native-dispatch-full-unit-results`). Separate workflow acceptance: one passed (`native-dispatch-acceptance-results`).
- The preceding project-merge run executed 2,980 tests with one stale package-count assertion; that assertion now checks the exact required closure length plus the five ordered consumer commands. No tests were disabled. Both earlier failure runs remain recorded.
- Reviewed source API baseline changes were limited to indexes 0 and 1, now Core and Json. The previous baseline is retained in `public-api-before-core-project-merge.txt`. No subsequent public declaration changes were needed for native dispatch.
- Static source pairing (`native-dispatch-pairing.json`) identifies six current test files referencing FlowOutput, including both direct fanout suites. Historical release-checkout matches were excluded from this interpretation; this is not line/branch coverage.
- Scoped whitespace validation passed. No package was published or promoted to candidate. Performance comparison, the source-protocol boundary, and unifying direct/application fanout remain required.

### Deadline settlement checkpoint

The shared coordinator now compares its recorded deadline under the existing settlement lock. It cannot resolve expired feedback just because the timer callback has not run. Three deterministic cases separate clock advancement from callback execution: before the deadline resolves, at/after it times out. The two late cases failed on the previous code and now pass, with capacity recovery and single-settlement assertions. Coordination: 18 passed; direct Core/request-reply: 18 passed (`exchange-deadline-results`, `exchange-deadline-core-results`). These scoped results follow the 2,982-case full dispatch checkpoint; a later full rebuild/rerun remains required. No additional registry, broker or timer was added.

## Canonical routing migration

WindowNode<T> now owns its operation directly over non-generic FlowNode; JsonWindowNode is only a constructor specialization. The old WindowNodeRuntime file and per-window canonical conversion wrapper path are removed. Component-local materialization and new-window serialization preserve the canonical port boundary. Additional output capacity now follows the explicitly configured routing capacity, as input capacity already did.

Routing suite: 47 passed (three new direct graph/error/materialization cases); existing routing composition suite: 13 passed. Nonincremental full solution build: zero warnings/errors (`canonical-window-full-build.log`). Reviewed declaration baseline changed only routing index 21, 222 to 206 declarations. Routing's development version is 8.0.0-alpha.1, documenting its intentional typed-port break; nothing was packed or published. The subsequent full unit run and correlation/join migration remain required.

Correlation now also owns its operation over canonical FlowNode. Its pending entries retain the original canonical message and a component-local typed value, without typed transport envelopes. The old runtime wrapper and the now-unused single-input canonical adapter are removed. Match/timeout JSON shapes remain unchanged; consumer tests deserialize the concrete expected outcome.

Routing suite: 49 passed (two additional direct matching/materialization cases and stronger reference-identity forwarding); composition suite: 13 passed (`canonical-correlation-results`, `canonical-correlation-composition-results`). Public declaration baseline again changed only routing index 21, 206 to 183 declarations, including removal of public members on the internal adapter. The subsequent nonincremental solution build passed with zero warnings/errors (`canonical-routing-full-build.log`); its full unit filter passed **2,990 cases, zero failed/skipped** (`canonical-routing-full-unit-results`). Static pairing associates each migrated component with its renamed test suite and JsonRoutingNodeTests, excluding historical checkout matches.

Join now exposes two actual canonical ActionBlock inputs and one canonical reliable output. Removed its typed/public runtime wrapper, both redundant input buffers, command-kind switch and the remaining canonical routing adapter file. One ordered native command block coordinates its state, with immutable command records carrying local execution delegates. Pending state holds canonical source envelopes plus locally materialized values. Matches/timeouts retain their JSON shape. Fault completion now waits for input/command work and outputs, preserving the first failure before resource disposal.

Final scoped routing suite: **54 passed**, including direct two-port Core request/reply, malformed input recovery, both-side exact error forwarding, active-selector disposal ordering and bounded input backpressure. Existing configured routing suite: 13 passed (`canonical-join-composition-results`); `canonical-routing-final-results` is the latest runtime scope. Join completion tests now await the authoritative completion and assert the original exception instead of requiring premature synchronous fault completion.

The routing declaration review changed only index 21 from 183 to 154 declarations. Fresh nonincremental full build: zero warnings/errors (`canonical-join-full-build.log`). Full unit filter: **2,995 passed**, zero failed/skipped (`canonical-join-full-unit-results`); separate workflow acceptance: one passed (`canonical-routing-acceptance-results`). Solution discovery includes the new output/deadline/routing cases (`canonical-routing-discovery.log`). Final pairing credits JoinNodeTests and JsonRoutingNodeTests for Join, excluding historical paths. Scoped whitespace validation passed. Optional binding/editor migration and whole-framework duplicate-runtime removal remain open.

## Canonical evaluator and observability migration

AssertionNode<T> and FlowMapperNode<TInput,TOutput> now derive directly from canonical FlowNode. Their JSON variants are constructor specializations, with no duplicated processing implementation. Host evaluators, context factories, options and clocks remain outside Core. Runtime suites passed 11 and 12 cases respectively, including direct request/reply, malformed materialization recovery and exact upstream-error forwarding. Existing composition suites passed 13 cases each.

Consolidation exposed a shared JSON projection issue: generic deserialization reparsed already-owned storage. The new default-projection regression first failed; default JsonElement projection now retains owned storage after source-document disposal, while an explicit-options regression proves custom converters still run. Foundation suite: 117 passed. Composition identity assertions were retained unchanged. This is not a measured allocation or throughput claim.

Reviewed declaration changes were limited to Core index 0 (385 declarations, signature normalization for the block-bodied projection), assertions index 17 (42 to 39), and mapping index 15 (36 to 31), recorded against `public-api-before-canonical-evaluators.txt`. Both component families use unpublished 8.0.0-alpha.1 development versions. Nonincremental solution build: zero warnings/errors (`canonical-evaluators-full-build.log`). Its full unit filter executed 3,003 cases: 3,002 passed and one release metadata check failed because the new family versions lacked their exact changelog headings. Those headings were added; the failure evidence remains and the next full rerun must pass it.

Counter, logger and measurement components now also derive directly from canonical FlowNode. Removed ObservabilityPipeline and CanonicalObservabilityNode, including their typed runtime and response/event relay queues; deleted tracked files remain recoverable through Git. Non-generic names are constructor specializations. Local selectors, predicates, clocks, counts/rates, templates, partial-failure diagnostics and result shapes remain in the optional family, not Core. Malformed typed input is rejected before counters or sequence state advances.

Observability runtime: **25 passed** (`canonical-observability-final-results`), including nine per-family direct graph/materialization/error cases plus bounded input and active-selector disposal cases. Existing host bindings: **24 passed** (`canonical-observability-composition-results`). The initial direct request tests omitted the required RepliesFrom declaration; corrected the test setup, not the runtime guard. The family now uses unpublished 9.0.0-alpha.1. Its reviewed declaration baseline changed only index 27 (135 to 89), recorded against `public-api-before-canonical-observability.txt`.

Static pairing associates each migrated implementation with its current family suite, and FlowValue with its foundation suite; historical release-checkout matches are excluded. Pairing is not measured coverage. Assertion audit checks that omitting materialization, advancing state before conversion, copying upstream errors, or completing disposal while a selector is active would violate explicit assertions. No executed mutation score is claimed.

Verified combined checkpoint: nonincremental Release solution build passed with zero warnings/errors (`canonical-observability-full-build.log`); **3,014 unit-filter cases passed**, zero failed/skipped (`canonical-observability-full-unit-results`); separate workflow acceptance passed one case (`canonical-observability-acceptance-results`). Solution-level discovery lists the new evaluator, owned JSON and all observability cases (`canonical-observability-discovery.log`). Only the old fluent execution path still references the generic node/source bases in current production source; authoring and operational integration remain open.

## Direct fluent runtime migration

The pipeline fluent path now registers existing component instances on FlowApplication. Removed generic fluent builders/handles, executable descriptor factories, private service-provider/host creation and borrowed-output branch components. Segments are non-generic construction steps over the same graph. Branches resolve actual declared ports, and repeated sink instances retain Core fan-in. Unused outputs explicitly discard, preserving prior fluent terminal behavior without weakening Core's unmatched-route policy. FlowGraph delegates startup, stop and completion to Core; event observation uses bounded native targets and remains best-effort.

Runtime/hosting scopes: **28 fluent and five hosting cases passed** (`direct-fluent-final-results`, `direct-fluent-hosting-results`). Seven new cases cover exact fanout message identity, names, once-only build, invalid branch ownership, segment boundaries, unstarted owned disposal and the absence of host/container assembly references. The first migrated suite exposed missing explicit discard declarations; the first negative address assertion expected ArgumentException instead of the existing KeyNotFoundException and was corrected. No runtime guard was removed.

The local sample ran successfully with three uppercase outputs and all six parity-routed outputs (`direct-fluent-sample.log`). Fluent and hosting use unpublished 7.0.0-alpha.1 versions. Reviewed source-declaration changes were limited to fluent index 52, 34 to 27 declarations (`public-api-before-direct-fluent.txt`). Scoped whitespace validation passed; Git reported only line-ending conversion notices. Nonincremental full build passed with zero warnings/errors (`direct-fluent-full-build.log`); **3,021 unit-filter cases passed**, zero failed/skipped (`direct-fluent-full-unit-results`), and the separate acceptance case passed (`direct-fluent-acceptance-results`). Solution discovery includes all seven new fluent cases. Static pairing links the new test suite to the actual fluent entry/graph/segment sources; this is not measured coverage.

All current production node/source consumers now use the canonical bases. The old generic base classes still exist until remaining tests, samples outside the fluent scope, benchmarks and edge helpers are migrated. Typed application ports, direct/application delivery duplication, portable authoring and optional operational capabilities remain open gates.

## Shared request/reply integration

The existing Core exchange adapter is now `FlowRequestReply`, used by both direct applications and optional host ports. One bounded coordinator owns fresh exchange correlations, expected reply addresses, finite deadlines, cancellation and shutdown. Explicit infinite requests have no heap deadline but still consume bounded pending capacity. The optional host no longer registers requests in trace-filtered receive waiters; those obsolete filters were removed. Ordinary receives remain a separate host capability pending the wider application-port consolidation.

Replies become eligible only after the output's optional capture step succeeds. Host output completion/failure cancels its waiting request; host shutdown stops the shared service. Typed host requests/responses are projected at the canonical boundary and retain values, identity metadata and headers, not typed-wrapper reference identity. Canonical service replies and owned payloads retain exact reference identity. No compatibility registry, timer or parallel request broker was introduced.

Focused evidence: coordinator 20 passed (`infinite-exchange-final-results`), Core 25 passed (`shared-core-exchange-final-results`), nine new host cases passed (`shared-host-capture-results`). The two infinite-timeout regression cases failed before their fix. The first whole Engine run passed 178/179; the remaining assertion required the old typed reply wrapper by reference and was updated to full record equality plus exact headers/correlation checks for the intentional boundary change. Final Engine/solution verification and declaration review are still pending at this point.

Static assertion/mutation review: removing the fresh correlation permits same-trace cross-matching; removing the expected address lets the wrong-output test settle; settling before capture breaks the gated assertion; accepting capture failure breaks its completed-without-response assertion; scheduling infinite entries breaks the one-year fake-clock checks; failure to unregister on expiry blocks the capacity-reuse case. These are reasoned test checks, not an executed mutation score or measured coverage percentage.

Final checkpoint: all **179 Engine cases passed** (`shared-host-exchange-verified-results`). Reviewed declaration changes were limited to Core index 0 (385 to 386) and Engine index 10 (473 to 474), with the before-file retained as `public-api-before-shared-exchanges.txt`. Nonincremental full build passed with zero warnings/errors (`shared-exchange-full-build.log`). Whole-solution unit filter: **3,039 executed and passed**, zero failed/skipped (`shared-exchange-full-unit-results`); separate workflow acceptance: one passed (`shared-exchange-acceptance-results`). Solution discovery includes the added cases (`shared-exchange-discovery.log`). Static pairing finds the shared coordinator/service sources; host builder integration was also inspected manually. This remains an intermediate checkpoint, not completion of typed execution, delivery, authoring, operations or local package acceptance.

## Canonical operational fixtures and component template

Migrated the reporting example, health-check identity fixture, event-feed emitter and resource-contract prefix fixture to canonical physical ports and bases. Existing operations remain intact: 32 health tests, 89 reporting tests and all 179 Engine tests passed. The reporting example executed into `canonical-reporting-archive` and produced count 2 with source completeness still explicitly unknown (`canonical-reporting-sample.log`). No report/persistence policy moved into Core.

Removed the unused generic file-system recorder helper and its private node class after confirming no callers; its active canonical fixture passed all 26 cases. The existing copyable component template now uses the canonical base, a component-local input model and the existing JSON materializer, with direct constructor/Core composition. Nine tests passed: four existing cases and five added malformed-shape, exact upstream-error and direct-composition cases (`canonical-template-results`). Its guide now describes the canonical ports and correct test path. This is migration of an existing sample, not the deferred new template-adapter feature.

These changes remove six consumers of the old generic bases but do not yet delete those bases, typed host ports or remaining binding/runtime duplication. The last whole-solution checkpoint above predates these final fixture/template changes; fresh full verification is pending here.

The subsequent full checkpoint passed: nonincremental build zero warnings/errors, **3,044 unit cases passed**, zero failed/skipped, one separate acceptance passed, and solution discovery included the five new template cases (`canonical-consumers-*`).

The durability operations example now also uses canonical physical ports and FlowValue persistence contracts while retaining its JSON string payload and independent string delivery handler. Its existing exact-output repeat-run test and updated source convention passed (`canonical-durability-sample-results`, two cases). The first build exposed an internal value converter inaccessible to external generated contexts; the existing converter is now public. Six generated-value cases then exposed JSON null returning a null reference; explicit null handling now preserves FlowValue.Null while still writing optional null references as JSON null. That regression failed before its fix; all **123 foundation cases passed** afterward (`generated-value-final-results`). This does not establish source-generation support for whole envelopes/generic helpers or whole-library ahead-of-time compatibility. The latest Core declaration review and full rerun remain pending here.

## Generic node/source retirement

Migrated concurrency and assembler fixtures, durable-input recording/drain fixtures, the composition sample, benchmark components and package-consumer source to canonical bases/physical ports. Typed client projections remain boundary helpers. The deliberately incompatible test ports use actual native blocks so contract rejection/generation replacement remain tested without the old generic runtime. Composition's unused shared registry and five old-base components were deleted after reference inspection; its one active node remains. Removed 653 lines of generic node/source implementation, moving the existing canonical implementations into FlowNode.cs and FlowSource.cs. Tracked removals are recoverable through Git.

Focused tests passed: concurrency 4, assembler 27, Composition **200**, durable input **157**, exact composition modes 2, documentation boundaries. The earlier commentary count of 182 Composition cases was stale; the actual current result is 200. Canonical durable targets initially rejected persisted string contracts before conversion; the regression failed before allowing canonical target projection, while malformed/incompatible contracts remain rejected. Both sample modes produce exactly ALPHA/BETA. The JSON mode uses a host-provided text projection, not a runtime model dependency. Package-consumer source changes still await actual local-package execution.

Nonincremental solution build: zero warnings/errors (`generic-base-removal-full-build.log`). Reviewed declaration change: only Core index 0, 388 to 351 (`public-api-before-generic-base-removal.txt`). Static source pairing includes the current bases and durable dispatcher; historical source matches are excluded, and no coverage percentage is claimed. Nine migrated host benchmark cases completed a scoped Dry run; the initial root-directory attempt executed zero because it discovered the frozen checkout. Existing typed client workload/conditional behavior was preserved, but no after-speedup claim is made. Full unit/acceptance/discovery is pending. Typed application ports, duplicate delivery and portable/operational boundaries still prevent completion of the goal.

## Native source-protocol correction

Two regressions using the actual filtered-link extension failed before implementation: its fast offer received a null source, and a postponed target never received its offer. FlowOutput now delegates each live offer's source callbacks to native SendAsync through a small internal adapter, with unique external headers and no payload queue, scheduler, timer or independent waiter registry. Active delivery snapshots retain reserved offers until native settlement after unlink. Filters can decline nonmatches without exhausting MaxMessages; permanently declining targets remain fatal. Reserved-message behavior follows native source ownership, not forced cancellation of a target's reservation.

Focused results: **129 foundation cases and 25 Core cases passed**, including six added filter/postponement/reservation/reoffer/unlink/duplicate-target/limit cases. Existing sibling-offer, permanent rejection, cancellation, bounds, drain and exact-reference forwarding assertions remain. This adapter adds per-offer allocation/state, so no allocation improvement is claimed; matched performance verification is still a gate. The full nonincremental build passed with zero warnings/errors (`native-source-protocol-full-build.log`). Declaration review and full tests are being finalized. The Core router and optional host still duplicate delivery policy; this correction alone does not consolidate them.

Final protocol checkpoint: nonincremental build zero warnings/errors; **3,058 unit cases passed**, zero failed/skipped; separate acceptance one passed; six new cases discovered (`native-source-protocol-*`). Reviewed declarations changed only Core index 0, 351 to 359, for internal helper members included by the source fingerprint (`public-api-before-native-source-protocol.txt`). Static pairing credits the current FlowOutput owner but not its internal offer helper directly; conformance tests exercise that delegation through the owner. Scoped whitespace validation passed. This is not a binary API or performance-improvement claim.

## Durable representation and host physical-port retirement

Durable intake now validates each registered payload contract, then restores the canonical envelope from its persisted JSON. This preserves default/custom names, generated metadata, converter output and extension fields without a second serialization. Error envelopes retain their existing restoration semantics. Five representation cases failed before the fix; the six new cases pass, including custom-converter rejection. Focused evidence: durable input 163, file provider 127, relational provider 69 on each net8.0/net10.0, and exact repeat-run sample one (`durable-canonical-shape-*`). Stored schema and provider behavior are unchanged; no external integration was run.

Migrated low-level host runtime/request fixtures (36 passed) and signal/handle/capture fixtures (30 passed) to native canonical physical ports. Typed handles remain client projections, including invalid projection followed by successful recovery; forwarding/capture assertions retain exact value/header references and complete identity. Removed generic physical input/output classes and their attachment/link/receive families, builder registrations and revision/binding branches. The existing canonical implementations now occupy the ordinary class/file names. Removed tracked code remains recoverable through Git.

Noncanonical component port metadata now fails during preparation before component construction. Both rejection regressions failed under the old accepted behavior. Unsupported revision attempts retain the active generation; valid integer client values still flow through that canonical generation. Legitimate surface-change generation tests remain. Durable invalid-transport coverage now asserts rejection with zero component factory calls. The first post-removal Engine run passed 178/179; the last test expected the detailed exception in the outer summary and was corrected to read the existing diagnostic detail. Full verification and declaration review are pending here.

This removes a duplicate physical-port runtime, not all remaining execution duplication. Core versus host routing/dispatch, the typed observation pump, capture/binding boundaries, optional authoring/operations, performance and local package acceptance remain open.

Final retirement checkpoint: full nonincremental build zero warnings/errors, **3,064 unit cases passed**, zero failed/skipped, and one separate acceptance passed. Discovery includes the six representation regressions and both preparation guards. Reviewed declaration changes affect only Engine index 10, 474 to 421 (`public-api-before-host-port-retirement.txt`). The affected source set lost 1,148 lines of duplicate runtime code. Static pairing maps current input/output ports, client runtime and durable contract tests, but not preparation directly; those guards and transitive tests were inspected manually. Scoped whitespace validation passed (`canonical-host-port-retirement-*`). This is not performance or final package acceptance.

## Native client observation and canonical receives

The existing optional observation wrapper now owns one bounded native TransformBlock from canonical messages to the requested client model. Removed the canonical-helper buffer, second typed buffer and manual conversion pump with its duplicate overflow logic. A small internal observation contract retains heterogeneous client projections without another queue or subscription broker. Projection errors remove/fault only that consumer; normal completion/disposal drains accepted items. Ordinary receive waiters now retain the original canonical message in an immutable outcome and project once when returning the client result. One small edge helper also serves request/reply projection; Core and component transport remain unchanged.

Focused verification: all **182 Engine cases passed**. The native-block regression failed before implementation; projection failure/error identity cases passed before and after. An existing direct waiter assertion failed on old envelope reference conversion, then passed when the canonical outcome replaced it. Existing capture, bounded overflow, healthy siblings, cancellation, drain and revision tests remain. Full build/declaration/regression verification is pending (`native-observation-*`, `canonical-receive-before-*`). Queue removal is established by code and native-block tests, not a measured allocation or throughput improvement.

Final client-boundary checkpoint: full nonincremental build zero warnings/errors; **3,067 unit cases passed**, zero failed/skipped; one separate acceptance passed, and discovery completed (`native-client-boundary-*`). Declaration review changed only Engine index 10's fingerprint, still 421 declarations (`public-api-before-native-client-boundary.txt`). Static pairing recognizes PortObservation through the concurrency fixture but misses the new projection helper/direct suite; those paths were inspected manually. Scoped whitespace validation passed. This does not establish performance or local package acceptance.

## Direct fluent optional dependency boundary

Moved PortableApplicationExtensions.BuildApplicationAsync to the Json package and namespace without a forwarding shim. Direct Fluent now references Core only; its restored library closure contains Core plus the native dataflow dependency. Removed unused optional dependencies from the two test projects. JSON execution parity and delegate-export rejection remain unchanged. Portable model/parser relocation and module/editor separation are still pending.

The strengthened compiled-assembly boundary assertion failed before the move. Focused results: Fluent 28, Json seven and hosting five passed; the real Fluent sample ran successfully. Full nonincremental build: zero warnings/errors; **3,067 unit cases passed**, zero failed/skipped, plus one separate acceptance case. Discovery completed (`fluent-optional-*`). Declaration review changes only Json index 1 (9 to 11) and Fluent index 52 (27 to 25); previous rows are saved in public-api-before-fluent-optional-boundary.txt. Scoped whitespace validation passed. This establishes a dependency cut, not completed authoring/runtime consolidation or package acceptance.

## Condition parity and native async boundaries

Canonical hosted condition variables now match the direct document path: input/payload contain message.Value, while error data remains on message.Error. A real typed evaluator formerly rejected error objects substituted into FlowValue parameters before a short-circuit condition could execute. Both host regressions failed on that mismatch; the two direct document cases passed. The initial test harness incorrectly accessed a typed error's Value property and was corrected before proving the real regression. Documentation records the intentional breaking alias correction. Focused results: Engine 184, Json nine, evaluator seven and scoped conditions ten passed.

The native output sender now returns the actual native send task, with offer cleanup in its existing owner finally block. Combined dispatch/snapshot cleanup removes redundant nested async wrappers without adding a queue, target-type switch or custom one-shot sender. All 129 foundation and 25 Core cases passed, including native filter/reservation, bounded fanout, cancellation, link limits and identity behavior.

Full nonincremental build: zero warnings/errors; **3,071 unit cases passed**, zero failed/skipped, plus one separate acceptance; discovery completed (`native-async-*`). Reviewed declaration change is only Core index 0's fingerprint at the same 359 count, reflecting the internal sender's async modifier (`public-api-before-native-async-boundary.txt`). Static pairing maps the output owner and host capture boundary but misses the new condition cases and internal helper classes, inspected manually. Scoped whitespace validation passed.

Nine Dry and nine matched Default measurements completed before the async cleanup. They show higher allocation and latency than the initial baseline, especially in larger graphs, with significant timing variance. The original baseline lacked subsequently verified native source-callback correctness, but that does not automatically justify the regression. Detailed evidence is in artifacts/core-refactoring/delivery-measurement-2026-09-08.md. The post-cleanup repeat is pending; no performance acceptance or completed routing consolidation is claimed.

## Expanded runtime matrix and invocation binding

Foundation and Core tests now permanently target both supported runtimes. The net10 benchmark's actual loaded Dataflow module is the shared runtime assembly from 10.0.11, not proof of exercising the central 9.0.4 package declaration. The net8 test dependency file and copied module verify the separate 9.0.4 package path; all 129 foundation and 25 Core cases passed on each framework.

The structured-expression adapter now serializes invocation variables once and supplies independently cloned native bindings from that parsed snapshot. Eight new cases preserve root/binding consistency, repeated evaluation, native mutation isolation and JSON shapes; the getter-count case failed before implementation. All 19 adapter cases passed. No host-native configuration or evaluation isolation was removed.

Full nonincremental build: zero warnings/errors; **3,233 unit executions passed**, zero failed/skipped, plus one separate acceptance (`binding-matrix-final-*`). The earlier expanded run failed one stale benchmark inventory and one timing-dependent MQTT test. The inventory now validates all twenty workloads; the MQTT case uses explicit gates, verifies both output order and lineage, and passed ten focused repeats. Production MQTT behavior was unchanged. Failure evidence is retained under binding-snapshot-full-unit-results.

Both matched binding workloads completed Dry and Default runs. Allocations dropped from 38,744 to 27,952 bytes/op for 8 items and 999,896 to 671,456 for 256 items. Timing variance is too large for a general speedup claim. See artifacts/core-refactoring/binding-measurement-2026-09-08.md. The delivery async-cleanup repeat also completed; it reduced allocations relative to the immediate pre-cleanup implementation, but timing was mixed and the initial delivery baseline remains faster/leaner. That larger regression is not accepted automatically; delivery-measurement-2026-09-08.md contains all three checkpoints. No local package candidate or completed architecture/operations acceptance is implied.

## Shared address ownership

Moved the existing immutable ApplicationAddress and its kind enum from Composition into Core; callers now import `FluxFlow.Core.Addressing`. The absolute/local port syntax, resource/system address rules, ordinal comparison and validation semantics remain. Direct graph and component port names reuse Core's segment validation. Current source, tests, samples and relevant docs were migrated without a duplicate type or forwarding alias. The old source paths were relocated, not discarded; historical release artifacts remain unchanged.

The compiled assembly-ownership regression failed on the old Composition-owned type. All 45 Core/address cases then passed on both net8.0 and net10.0. Full nonincremental build zero warnings/errors; **3,254 unit executions passed**, zero failed/skipped, plus one separate workflow acceptance; discovery completed (`core-address-*`). Reviewed source-baseline changes are Core index 0, 359 to 379; Composition index 5, 536 to 516; and durable-input index 54's same-count fingerprint due to its fully qualified internal helper signature. Previous rows are retained in public-api-before-core-address-ownership.txt. All dependent binaries must be rebuilt for this intentional namespace/assembly change.

Static pairing recognizes the address implementation through the moved suite and its consumers. It does not identify FlowTopology directly; existing direct graph tests were inspected and passed. Scoped whitespace checks passed. This ownership move removes one obstacle to document separation but does not complete portable authoring, shared routing/lifecycle, operational verification or package candidate acceptance. Address lookup indexing and the wider delivery-performance regression are still separate work.

## Earlier verification checkpoints

Final generic-base retirement checkpoint: nonincremental build zero warnings/errors; **3,052 unit cases passed**, zero failed/skipped; one separate acceptance passed and discovery completed (`canonical-runtime-final-*`). The earlier convention failure below is resolved. This verifies retirement of generic node/source implementations, not completion of the remaining host/runtime/authoring/operations/package gates.

Initial post-removal unit run: **3,052 executed, 3,051 passed, one failed**, zero skipped. The failure was the package-consumer source convention requiring typed handle call-site syntax; updated it to require canonical physical declarations and typed projection by address, preserving the independent scenario/resource/health assertions. Focused convention rerun passed; full rerun remains pending. Separate acceptance passed one case. Scoped whitespace validation passed with line-ending notices only. This evidence remains an intermediate checkpoint.

The subsequent persistence checkpoint passed: nonincremental solution build zero warnings/errors, **3,050 unit cases passed**, zero failed/skipped, and one separate acceptance case (`canonical-persistence-*`). Solution discovery completed. Reviewed source-declaration changes were limited to Core index 0, 386 to 388, for the existing converter's accessibility and null-handling override (`public-api-before-generated-values.txt`). No whole-envelope serialization, final package or performance acceptance is implied.

## Measurement results and caveats

The initial eight dry cases passed after adding a scoped benchmark solution. The expanded twelve dry cases also passed (nine canonical hop/fanout combinations plus three application hop counts). Dry results are validation, not performance data.

The 18-case Short run used three warmups and three measurement iterations. Several application-path confidence intervals are wide. Treat it as an initial diagnostic measurement, not an acceptance budget or statistical improvement claim. Standalone canonical delivery and application request/reply are different workloads and cannot be presented as a before/after comparison.

The scoped benchmark solution prevents discovery from walking into the frozen release checkout. The upstream [project discovery implementation](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Toolchains/CsProj/CsProjGenerator.cs) searches beneath the nearest solution root; changing the working directory alone did not isolate it. The frozen checkout was not modified.

Default baseline examples (same machine, Release/net10.0, no external services): canonical 1-hop/1-target delivery 3.888 microseconds and 1.15 KB/op; canonical 32-hop/8-target delivery 74.830 microseconds and 16.59 KB/op. Existing addressed request/reply at two hops measured 69.12 microseconds and 62.79 KB/op. These are distinct workloads, not relative speedup claims. Some higher-hop measurements reached high variance; repeat matched workloads before making performance acceptance decisions.
