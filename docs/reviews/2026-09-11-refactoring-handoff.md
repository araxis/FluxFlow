# Refactoring handoff and acceptance checklist

Status: **Approved local refactoring goal completed; integrated candidate qualified**.
The [integrated candidate](2026-09-12-integrated-local-candidate.md),
`0.0.0-local.20260912.integrated.1`, includes the integration-discovered source-event
shutdown correction. All 81 packages, 81 symbols, 162 assemblies and 36 fresh
package-only commands pass on both frameworks. Final source regression: 4,868
passes with 1,492 stable inputs. Real-service execution passes 90 durable-input,
117 durable-output and one broker/HTTP workflow case; in-process acceptance also
passes. Integration production DLL copies match the new packages. Containers are
removed. External endpoint smoke is separately configured and unrun, not a passed
test. Publication and FluxMQ-NG coordination still require separate authorization.

Historical measurements below retain their original build identities.
The [shutdown correction and loaded termination](2026-09-12-host-termination-load.md)
qualified their source checkpoint with a clean build, 4,864 local passes and 1,492 stable
inputs. Three permanent cases cover the error-observation race. Four smokes/eight
measured runs cover 5,137,288 timed replies and 1,024 held stop/fault requests; all
32 owned nodes dispose once and become collectible. This changes optional Engine,
not Core. Prior evidence remains attached to its original builds.
The [HTTP queued-context correction](2026-09-12-http-context-boundary.md) is verified
from source on both target frameworks, including real loopback transport.
The frozen [context.1 candidate](2026-09-12-context-candidate.md) includes the
[MQTT transport-context correction](2026-09-11-mqtt-context-boundary.md)
but predates the HTTP correction. Both previous feeds remain unchanged.
This is the consolidated index for the seven deliverables in the approved
implementation prompt. Historical reports
retain their original source/build identities; later evidence below supersedes
their outstanding-item lists only for the specifically verified scope.

The [consumer telemetry timing](2026-09-12-consumer-telemetry-cost.md) now closes
the representative SDK off/on gate: four smokes/eight measurements, 4,471,467
timed replies, unchanged qualified sources. The measured consumer configuration
costs 15.6–20.0% throughput and 24.6–25.6% additional allocation/request. Exact
trace/log totals pass; best-effort diagnostic admission can omit duration samples,
so the duration histogram count is not a lossless business-request counter.
The [final reconciliation](2026-09-12-final-acceptance-reconciliation.md) now closes
the local structural/behavior and representative performance review. A fresh
in-process workflow acceptance case passes; the later integrated report closes
the real-service and refreshed-package portions after the user enabled the local
engine and confirmed the database license. No further generic performance matrix
is required by the approved representative scope.

Performance scope: ordinary applications, not online gaming or competition with
actor frameworks. No numerical application SLO was supplied. Favor simple code,
reliable delivery, bounded resources and measured responsiveness. Do not add
optimization machinery merely to maximize benchmark throughput. This clarification
does not waive investigation of measured regressions or bounded-state checks.

## 1. Public API and working examples

The package-only [minimal consumer](../../artifacts/core-refactoring/local-candidate-20260912-context/minimal/Program.cs)
is the executable example. It verifies standalone construction, cross-workflow
conditional fanout and eight simultaneous requests sharing one trace. The calls
below are excerpts from that compiled fixture, not a separate executable listing:

```csharp
var source = app.AddWorkflow("main").Add("relay", new Relay());
var reports = app.AddWorkflow("reports");
var left = reports.Add("all", first);
var right = reports.Add("positive", second);
source.Output.LinkTo(left.Input);
right.Input.LinkFrom(source.Output, message => message.Value!.Deserialize<int>() > 0);

// In the request/reply example, after adding the increment node:
app.RepliesFrom(node.Output);
await app.StartAsync();
var replies = await Task.WhenAll(inputs.Select(input =>
    app.RequestAsync(node.Input, node.Output, input, Deadline)));
await app.StopAsync();
```

The component's implementation is local `FlowNode.ProcessAsync`; forwarding emits
the existing message, while a transformation uses `message.With(...)` for derived
identity. No creator or registration descriptor is needed by this direct path.
The fixture checks reference identity, exact results, independent exchange IDs,
drain and once-only disposal. See its [verified results](../../artifacts/core-refactoring/local-candidate-20260912-context/minimal/qualified/verification.json).
The context-aware Add overload remains deferred.

## 2. Package structure and runtime ownership

| Boundary | Current placement |
| --- | --- |
| Canonical messages, ports, graph, delivery and exchange primitives | Core; no outward project references |
| Direct fluent authoring | Fluent → Core |
| Portable workflow documents | Json → Core + Mapping |
| Optional contracts, resources and document construction | Composition → Core + Json + Mapping |
| Revision operations, stable ports, durability and health | Optional Engine and its extensions/providers |
| Component families | 19 runtime families, each with separate optional Composition and Designer packages |
| Evaluator implementations and editor support | Optional packages, never required for direct Core construction |

The manifest contains 81 packages, compared with 62 originally: 19 separate editor
packages were added; former Nodes and Coordination responsibilities were moved
into Core. All 19 ordinary family Composition dependency closures exclude Designer.
The [component matrix](../17-component-coverage-matrix.md) gives the full family map.
The [execution-owner trace](2026-09-11-runtime-execution-owners.md) distinguishes
shared Core delivery/lifetime/exchange implementation from legitimate optional
revision queues and transactions. An update-enabled host has extra operational
cost; sharing the implementation does not make that cost disappear.

## 3. Migration ledger and breaking changes

Use the [major migration guide](../46-code-first-major-migration.md) for client
changes, including canonical physical ports, package/namespace moves, explicit
discard/reply intent, document conditions and resource construction.

| Accounting | Verified scope | Limitation |
| --- | --- | --- |
| Component types | All 60 original types map to 56 current types; four generic timer/retry consolidations | Type presence is not all-operation parity |
| Constructors and configuration | 58 unchanged constructor mappings; two explained changes; no captured public port names missing; 176/178 options/contracts files unchanged | Bounded baseline inventory, not every public method |
| Removed source paths | All 83 have replacements or explicit retirements, including initially untracked paths | Path correspondence is not behavior equivalence |
| Persistence | 53 provider source files: 43 unchanged, ten namespace relocations; canonical storage boundaries reviewed | Final server-provider suites: 90 input and 117 output cases passed |

Evidence: [contracts](2026-09-11-component-contract-migration.md),
[removals](2026-09-11-source-removal-audit.md),
[persistence](2026-09-11-persistence-migration-qualification.md).
The follow-up reports below reconcile local per-family processing, errors, timing
and lifecycle. They do not turn unavailable external integration or remaining
cross-cutting/performance checks into passed gates.

Follow-up [authoring behavior qualification](2026-09-11-authoring-behavior-qualification.md)
closes the specifically mapped authoring/evaluator configuration, editor persistence
and health-readiness checks: 666 fresh executions across eight suites, all 1,488
source inputs unchanged, 32 loaded dependency copies matched to the candidate.
The report maps requirements to actual assertions and identifies the remaining scope.

The [seven-family behavior qualification](2026-09-11-component-behavior-qualification.md)
reconciles Serialization, Assertions, Payloads, Validation, State, Projections and
Sessions: 92 runtime source comparisons, ten explicitly reviewed changes, 263 fresh
passes across 14 suites, unchanged qualified inputs and 79 matched dependency copies.
The component matrix now credits these rows without waiving other acceptance work.

The [remaining-family qualification](2026-09-11-remaining-family-qualification.md)
and prior storage review finish the local family dispositions: 882 further fresh
executions, 218 runtime paths accounted for and all 19 selected retirements linked
to current replacements. All ordinary families and additional transport/bridge
surfaces now have local behavior evidence. External and cross-cutting performance
acceptance remain separate.

## 4. Build, tests and integration evidence

The latest [source qualification](../../artifacts/core-refactoring/real-integration-20260912/final-complete/verification.json)
covers 1,492 inputs: clean nonincremental build, 4,868 local passes, 81 report files
(76 nonempty suites and five explicitly excluded integration/acceptance suites).
The frozen context.1 candidate's nine existing MQTT context cases pass from
packages on both target frameworks; broker forwarding is simulated. These reruns
do not increase the unique solution count. The later HTTP source fixture passes
ten cases per framework, including four real loopback checks. Only its four
permanent queue-context regressions increased the solution count. Fresh unfiltered
discovery was not run. The qualified.1 candidate now reruns those HTTP cases and
the shutdown smoke scenario from packages on both frameworks; see the latest
candidate report above. The historical observations below retain their scope.

The preceding startup.1 source qualification covered 1,488 inputs. Its
[command record](../../artifacts/core-refactoring/activation-rollback-20260911/qualification-final/verification.json)
contains the exact build, regression, discovery and sample arguments.

- Nonincremental Release solution build: zero warnings/errors. A later fresh
  solution build during signal qualification was also clean, with sources unchanged.
- Selected local regression: **4,848 passed**, zero failed/skipped, 81 reports.
- Full discovery: **5,058 cases**. Discovery is not execution. The local filter
  excludes Acceptance, ContainerIntegration, ExternalSmoke and TSql integration.
- Separate authored acceptance: one passed; supplied transport input/in-process
  HTTP handler, not a real broker/socket integration.
- Later focused persistence suites: 766 passed; direct constructor/bridge suites:
  eight passed. These overlap the local suite and must not inflate its unique count.
- Package-only in-flight updates: four runs, 128 update attempts, 2,176 exact replies,
  old/new routing ownership and rollback/cancellation checked.
- Consumer signal collection/export: four runs across both frameworks, 64 requests,
  correlated logs/spans/events and metric totals verified using in-memory export.

See [update qualification](2026-09-11-startup-candidate-and-inflight-revisions.md)
and [signal qualification](2026-09-11-consumer-signal-qualification.md).
Final real-service conformance is recorded in the integrated report: 208 cases
pass on net10.0 after the user enabled the engine and confirmed the database
license. This is source-suite execution with package-equivalent production DLLs,
not package-only or net8.0 server conformance. No remote endpoint was assumed.

## 5. Performance: measurements, not a general speedup claim

Latest [loaded host termination](2026-09-12-host-termination-load.md) closes the
representative load-followed-by-held-stop/fault scope, including a verified fix
for dropped component errors. It does not claim arbitrary external ingress races,
timed SDK overhead or final-package acceptance. Raw throughput, percentile,
allocation and termination measurements retain their configuration and caveats.

Previous [active-host retirement](2026-09-12-active-revision-retirement.md): eight
fresh stable/changed-address runs, 128 replacements, 2,176 exact replies, 64 removed
address rejections and 136 once-disposed components. Prior probes clear while the
active host continues answering; final probes clear after disposal. Four smokes,
clean builds, 1,491 stable sources and ten current-build-equivalent package DLLs.
This closes the selected live-host retirement scope with scheduling intervals,
not stop/fault load, timed SDK overhead or instantaneous/universal leak freedom.

Previous [observer-retirement qualification](2026-09-12-observer-retirement.md): four
fresh processes complete 512 requests through 32 application lifetimes with a
consumer SDK kept alive. All 128 application/provider/component/native-target
probes clear, followed by all 16 SDK/provider probes after SDK disposal. Explicit
drain, unlink and flush precede collection. Ten consumed DLLs match current build;
1,491 sources remain unchanged. This closes that disposal boundary, not timed SDK
overhead, active-host revision retirement or universal leak freedom.

Latest [optional-host live request load](2026-09-11-live-request-load.md): four measured
processes completed 2,426,077 matching replies through 48 compatible replacements;
every revision processed data, public queues drained and all 52 component probes
cleared after once-only disposal. Request p99 upper bounds were 1.285–1.595 ms;
whole-workload allocation was 22,111–23,049 bytes/request, not Core-only cost. Two
final smokes and clean builds passed, with unchanged sources/candidate. This closes
representative successful update load and selected post-disposal retention, not
fault load, incompatible port changes, live-host or SDK observer retention.

Latest [representative payload/fanout repetitions](2026-09-11-payload-fanout-repetitions.md):
12 smokes and 24 measured current-build processes, two repetitions per scenario/
framework, 67,640,294 completed messages and 282,755,940 exact target deliveries.
The existing harness checks original-envelope identity at every target. At 32 hops/
eight targets, increasing structured/binary data from 256 to 65,536 changed mean
whole-workload allocation by −1.66% to +1.10%, with no payload-sized per-branch
growth observed. Report both repetitions' throughput and latency variation; this
does not erase the native-link regression below. Source/runtime/package identities
are unchanged. The outstanding representative payload repetition item is closed.

Latest [direct request failure/retention qualification](2026-09-11-request-expiry-fault-retention.md):
four repeated current-package processes across both frameworks completed 32,768
expiries, 1,024 recoveries and 8,192 faulted requests, with 256 exactly-once owned
disposals. All 1,536 selected retired payload/application probes became collectible
at 96 measured checkpoints. Two final smokes and 76 existing focused tests passed;
fresh nonincremental solution/consumer builds are clean and 1,488 sources unchanged.
Observed latency limits and heap variability are explicit; this closes representative
direct expiry/fault/collectibility work, not optional-host or observer retention.

| Experiment | Before → after | Interpretation |
| --- | --- | --- |
| Compiled binding, 8 items | 38,744 → 27,952 bytes/op; 13.548 → 13.592 µs mean | Lower allocation; no timing improvement established |
| Compiled binding, 256 items | 999,896 → 671,456 bytes/op; 334.674 → 291.245 µs mean | Lower allocation; substantial timing variance |
| Historical delivery, 1 hop / 1 target | Original 3.888 µs / 1,176 bytes → cleanup 6.005 µs / 1,401 bytes | Regression, not automatically accepted |
| Historical delivery, 32 hops / 8 targets | Original 74.830 µs / 16,993 bytes → cleanup 144.549 µs / 29,915 bytes | Regression; original lacked subsequently tested native protocol correctness |

These matched Release/net10.0 experiments used runtime 10.0.11/SDK 10.0.400.
They are historical checkpoints, **not fresh measurements of startup.1**. Details,
variance and commands: [binding](../../artifacts/core-refactoring/binding-measurement-2026-09-08.md),
[delivery](../../artifacts/core-refactoring/delivery-measurement-2026-09-08.md).
Later isolated-fix comparisons are not a substitute for an original-to-final comparison.

The new [original-to-current delivery comparison](2026-09-11-original-current-delivery.md)
now supplies that evidence for three representative standalone topologies: 12
matched measured processes, two per implementation/topology in opposite pair
orders. Original source was reconstructed and content-verified; current generated
processes load exact startup.1 Core binaries. Means rose 3.589 → 4.160 µs (1/1),
22.813 → 27.903 µs (8/2), and 70.082 → 92.427 µs (32/8). Allocations rose
20.9–79.6%. This is a confirmed regression, not an overall speedup. Native
source-callback correctness contributes additional work; its complete cost has
not been isolated by profiling. The small bridge is retained, with the tradeoff
visible. Named application routes use a different admission path within the same
delivery owner. Historical cleanup results above remain historical.

Representative additional evidence includes eight 120-second slow-consumer runs
and eight ten-second direct request runs with cancellation/recovery and pending
shutdown checks. They have distinct candidate identities and workload limits.
The [performance coverage ledger](2026-09-11-performance-evidence-coverage.md)
separates correctness matrices, timed load, source identities and remaining gaps.
Retained heap after business drain alone is not proof of observation quiescence
or leak freedom. No production p99 or throughput guarantee is made.

The [current condition qualification](2026-09-11-current-condition-costs.md) adds
24 current-candidate cases / 48 measured process launches across both frameworks,
preceded by 24 smoke checks and two representative runs. The unchanged benchmark
executes against exact packaged dependencies. Native predicates are inexpensive
relative to these adapter workloads; adapter allocation/time costs are documented
explicitly. This closes the outdated-source condition gap. Together with the new
representative before/after batch it advances the performance gate; outstanding
optional-host, observer retention and lifecycle-load work remains.

## 6. Local candidate and consumer status

Latest packaged checkpoint: `0.0.0-local.20260912.integrated.1` for all packages.
It includes the source-event shutdown correction and matches current sources.

Feed: `C:\Users\meisa\.codex\worktrees\9851\FluxFlow\artifacts\core-refactoring\local-candidate-20260912-integrated\feed`.

The [candidate acceptance record](../../artifacts/core-refactoring/local-candidate-20260912-integrated/acceptance.json)
verifies 81 packages, 81 symbol packages, 162 target assemblies and 81 packaged
READMEs. All 162 all-package consumer DLLs match the qualified assemblies.
Ten all-package commands and twelve minimal-consumer commands passed across
net8.0/net10.0 with isolated restores, including separate-process durable recovery.
Four further commands verify nine existing MQTT context cases per target, four
commands verify ten HTTP cases per target including real loopback, and six commands
verify four shutdown smoke processes. All 36 commands pass. Ninety-six DLLs across
48 packages differ from qualified.1 within the changed dependency closure; the
other 66, including both Core binaries, are unchanged.
All historical qualified.1 package/symbol hashes remain verified. See the candidate
report for the exact source delta and corrected assembly inventory expectation.

FluxMQ-NG has **not been coordinated or verified as part of this refactoring goal**.
Its external acceptance is distinct from repository-owned package consumers and
requires separate authorization. Do not substitute the original FluxMQ project.

## 7. Completed acceptance and decision boundary

1. **Local acceptance reconciliation completed.** The final reconciliation maps
   all seven structural and eleven behavioral requirements to existing evidence
   and separates the remaining package and real-service portions. Do not reopen
   completed family, concurrency, telemetry or structural checks without a
   concrete changed dependency or newly identified defect.
2. **Representative performance evidence completed.** Matched repeated workloads,
   condition/payload/telemetry costs and bounded-state recovery have explicit
   dispositions. Preserve measured regressions and reliability tradeoffs. No
   arbitrary application SLO or full Cartesian product is required.
3. **Real-service integration completed.** Disposable broker/HTTP and server
   database suites pass on the final build, with source/DLL identity checks and
   container cleanup. The separate external-endpoint smoke remains unrun.
4. **Local candidate and handoff completed.** Both corrections and package-only
   consumers are qualified; historical feeds are preserved. The environment and
   licensing prerequisite is resolved. FluxMQ-NG coordination and publication
   remain separately authorized.

These tasks delimit the existing approved work; they do not authorize new template,
iteration/feedback, context-aware Add, reporting-engine or compatibility features.
Network telemetry backend operations are consumer/environment concerns, not an
excuse to add another collection system to Core. Verify library-owned signal and
transport boundaries without making arbitrary backend installation a new feature.

Nothing was published, committed, tagged, pushed or sent to an external consumer
task in this consolidation. The approved local goal is complete.
