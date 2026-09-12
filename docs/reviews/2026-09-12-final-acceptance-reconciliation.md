# Final acceptance reconciliation

This resolves the local structural, behavior and representative performance
checklist in section 6 of the approved implementation prompt. It does not approve
publication. The [integrated candidate](2026-09-12-integrated-local-candidate.md)
closes the final coherent-package and real-service integration gates, including
the source-event shutdown correction discovered during real-service execution.
Previously qualified checks are credited, not converted into an exhaustive matrix.

## Structural requirements

| Requirement | Disposition and evidence |
| --- | --- |
| Instance-first construction | `FlowWorkflow.Add(string, IFlowComponent, ComponentOwnership)` directly delegates ownership/topology registration. The executable minimal package consumer uses constructors and both link directions; no creator or descriptor is required. |
| One execution policy | [Execution-owner audit](2026-09-11-runtime-execution-owners.md): direct and revision-selected delivery meet in `FlowOutput.DeliverRoutesAsync` and `FlowDeliveryBatch`; both lifetimes use `FlowLifetime`; application exchanges use `FlowRequestReply` and `PendingExchangeCoordinator`. Optional revision queues/transactions remain explicit, not a second fanout algorithm. |
| Dependency and target boundaries | Core has no project references and retains net8.0/net10.0. [Minimal package qualification](2026-09-11-minimal-package-boundaries.md) and the manifest reconciliation establish that ordinary family registration does not require editor packages. |
| Local component/port extension | Components expose their actual local ports. Direct Add accepts `IFlowComponent`; optional document registrations are separate. [Contract migration](2026-09-11-component-contract-migration.md) accounts for constructors and ports without requiring a central component-type switch. |
| Forwarding path | Direct delivery uses prepared endpoint references and existing messages. [Authoring qualification](2026-09-11-authoring-behavior-qualification.md) locates expression compilation in preparation and verifies invocation isolation. [Payload qualification](2026-09-11-payload-fanout-repetitions.md) checks exact content/reference identity and allocation scaling. Native evaluator input conversion is not ordinary message forwarding. |
| Cohesive Core | Application lifecycle orchestration, topology validation, execution, lifetime, routing, delivery batches and exchange matching have separate implementation owners. This is a responsibility assessment, not a line-count claim or a promise that optional update hosting has no cost. |
| Full migration ledger | [Contracts](2026-09-11-component-contract-migration.md), [removals](2026-09-11-source-removal-audit.md), [ordinary families](2026-09-11-remaining-family-qualification.md) and [persistence](2026-09-11-persistence-migration-qualification.md) account for every reviewed family and all 83 removed paths. |

Current source was rechecked for Add, Core project references, Fluent output
policy, shared delivery and lifetime owners. The historical graph was used only
for navigation; it still contains removed registration types. Its edges are not
evidence about the present implementation.

## Behavior requirements

Numbers correspond to the approved prompt, not newly invented acceptance gates.

| Item | Local disposition | Evidence and boundary |
| --- | --- | --- |
| 1–2: construction and ownership | Qualified | [Authoring](2026-09-11-authoring-behavior-qualification.md), [resources](2026-09-11-portable-resource-boundary.md), [startup transaction](2026-09-11-candidate-startup-transaction.md): constructor counts, validation before startup, borrowed/owned resources, failure cleanup and repeated disposal. |
| 3: canonical data and identity | Qualified | [Component behavior](2026-09-11-component-behavior-qualification.md), [payload fanout](2026-09-11-payload-fanout-repetitions.md): error identity, disposed/mutated source ownership, exact bytes, immutable headers and derived identity. |
| 4: authoring parity | Qualified for the mapped supported behaviors | [Authoring qualification](2026-09-11-authoring-behavior-qualification.md) maps actual output/error/event assertions, direction, fallback, explicit policies, invalid configuration and nonportable export. The old implicit-Fluent-discard finding is superseded by its explicit-policy regression cases. |
| 5–6: delivery, completion and termination | Qualified | [Runtime owners](2026-09-11-runtime-execution-owners.md), [saturation](2026-09-11-sustained-saturation.md), [request lifecycle](2026-09-11-request-lifecycle-qualification.md), [loaded termination](2026-09-12-host-termination-load.md): bounded admission, partial fanout, cancellation, accepted-work drain and original fault preservation after delayed observation. |
| 7: request/reply | Qualified | [Lifecycle](2026-09-11-request-lifecycle-qualification.md), [expiry/fault retention](2026-09-11-request-expiry-fault-retention.md), current Core/Engine regression: independent correlations on shared traces, bounded registry, rejection, races, cleanup, recovery and shutdown. Protocol acknowledgement state uses the shared coordinator implementation but is not application first-reply matching. |
| 8: expressions | Qualified | [Authoring qualification](2026-09-11-authoring-behavior-qualification.md) and [condition costs](2026-09-11-current-condition-costs.md): engine scope, native options, cached compilation, parameters, concurrent isolation and invalid result/configuration failures. Trusted synchronous host functions are not sandboxed or preemptively cancellable. |
| 9: updates and persistence | Qualified, including final real-server source suites | [In-flight revisions](2026-09-11-startup-candidate-and-inflight-revisions.md), [active retirement](2026-09-12-active-revision-retirement.md), [persistence](2026-09-11-persistence-migration-qualification.md): stable/changed addresses, rollback/drain, owned cleanup, restart and existing durable state semantics. The integrated candidate adds 90 durable-input and 117 durable-output real-server passes on net10.0. |
| 10: diagnostics | Qualified at library and consumer-SDK boundaries | [Consumer signals](2026-09-11-consumer-signal-qualification.md), [fan-in](2026-09-11-concurrent-fanin-tracing.md), [HTTP context](2026-09-12-http-context-boundary.md), [MQTT context](2026-09-11-mqtt-context-boundary.md), [observer retirement](2026-09-12-observer-retirement.md), [SDK cost](2026-09-12-consumer-telemetry-cost.md). HTTP includes real loopback sockets; broker forwarding is simulated. Bounded best-effort diagnostics may omit duration samples under load; they are not a lossless business counter. Network collection/backend operations remain consumer concerns. |
| 11: packages, editor, health and integrations | Qualified for the approved local scope | The integrated candidate contains all current fixes: 81 packages and 36 fresh package-only commands on both targets. Editor persistence and health have assertion-level qualification; final real broker/HTTP and database source suites pass on net10.0. External consumer coordination and separately configured remote-endpoint smoke are not claimed. |

No context-aware Add, template or feedback/iteration feature is introduced or
required by this reconciliation; those remain explicitly deferred.

## Current checkpoint and fresh acceptance

Current regression qualification is **4,868 passes**, 76 nonempty suites,
81 reports including five explicitly excluded suites, and 1,492 source inputs.
The clean full build and source inventory are in the
[final integration checkpoint](../../artifacts/core-refactoring/real-integration-20260912/final-complete/verification.json).
The final nonincremental build and regression were rerun after the event-drain
correction. All source and report hashes are verified; earlier failed and aborted
attempts remain separate.

The fresh net10.0 acceptance case
`Authored_mqtt_value_maps_through_jsonata_to_http_and_preserves_flow_context`
passed once with no failures/skips. It verifies real component mapping and runtime
behavior, exact binary payload projection, request fields, invalid-input rejection,
trace/header/causation preservation and drain. Its HTTP recording handler and
constructed inbound message do not exercise a broker or network HTTP service.
This is separate from the 4,868 regression count. The integrated checkpoint reruns
it and additionally passes the original real-broker/HTTP workflow.

The historical [reconciliation verifier](../../artifacts/core-refactoring/final-acceptance-20260912/verify.ps1)
checked its source snapshot, regression reports, acceptance TRX and copied
production DLLs against that checkpoint's Release outputs. Its result is
[verification.json](../../artifacts/core-refactoring/final-acceptance-20260912/verification.json).
It is superseded for current identity by the integrated checkpoint's
[service verifier](../../artifacts/core-refactoring/real-integration-20260912/service-verification.json)
and [package acceptance](../../artifacts/core-refactoring/local-candidate-20260912-integrated/acceptance.json).

## Performance disposition

The [performance evidence index](2026-09-11-performance-evidence-coverage.md) and
linked reports now cover representative topology, payload, condition, telemetry,
saturation, request cancellation/expiry/fault, revision and retirement dimensions.
Their exact source identities, repetitions and limitations remain authoritative.
No exhaustive Cartesian matrix or competitive application SLO is required.

Meaningful costs were investigated, not hidden: the original/current comparison
reports increased time per operation and allocation for its matched cases;
the retained native delivery bridge supports required reliability semantics.
The installed consumer telemetry configuration costs 15.6–20.0% throughput and
24.6–25.6% additional allocation/request. Those measurements do not establish
universal performance, statistical confidence or application latency guarantees.
Scoped weak-reference retirement and bounded-state recovery are not a proof that
every possible user component is leak-free. No further generic performance matrix
is needed to close the approved representative scope.

## Final gate disposition

1. Completed by the [integrated local candidate](2026-09-12-integrated-local-candidate.md):
   all context and shutdown fixes included; all old feeds preserved; manifest,
   symbols, dependencies, target DLLs and fresh package-only consumers verified
   on both supported frameworks.
2. Completed after the user enabled the local container engine and confirmed the
   database image license. Final real-service tests pass: 90 input, 117 output and
   one broker/HTTP workflow. One in-process acceptance also passes. Source and
   test/runtime binary identities match the final checkpoint and local packages.
   Disposable containers were removed. These are net10.0 source-suite executions,
   not cross-target server or package-only integration. Separately configured
   external-endpoint smoke remains unrun, not passed or waived.

The approved local refactoring goal is complete. Separate authorization is still
required for FluxMQ-NG task coordination and publication. Do not reopen completed
representative checks or add deferred features merely to extend this goal.
