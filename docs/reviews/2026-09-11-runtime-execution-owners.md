# Runtime execution owners and remaining policy mismatch

This audit traces current implementation owners. It supersedes historical README
claims that hosted fanout has not moved to Core, but does not declare the entire
architecture or capability migration complete.

## Construction and execution paths

| Entry | Construction/preparation owner | Executed graph and ownership |
| --- | --- | --- |
| Direct instances | Caller constructors; `FlowWorkflow.Add` | `FlowApplication` / `FlowExecution`; owned or borrowed component disposal |
| Direct Fluent | `FlowGraphBuilder.Register` calls Core Add; shared instances indexed by reference | `FlowGraph.Application` is the same Core application; event aggregation is separate best-effort observation |
| Minimal document bindings | `FlowJson.BuildAsync`; trusted local constructors and `DocumentLinks` | New Core application, validated before startup; failures dispose newly owned instances |
| Documents with resource/contracts | `ContractApplicationExtensions.BuildApplicationAsync`; compilation/preflight, resource scope, component activation | Core application owns the resource scope and component graph views; no stable revision ports |
| Ordinary hosted graph | `FlowApplicationHostedService` receives an existing application | Starts/stops/observes that exact Core application; no revision preparation |
| Update-enabled host | `ApplicationRuntimePreparation` builds candidates, snapshots and port revisions | Canonical components, shared Core lifetime/fanout/exchanges; optional host owns port generations, staging and transaction barriers |

Minimal FlowJson intentionally rejects resource documents; the optional contract
construction path handles them. Its current exception calls that a migration gap.
Do not confuse lack of resource services in the minimal binding overload with
resource support being absent from the direct Core execution path.

## Queues, snapshots and lifetime

| Stage | Actual block/owner | Capacity and lifetime boundary |
| --- | --- | --- |
| Component processing | FlowNode's native action block; FlowTransformNode's transform block and forwarding stage | Component options; component owns active work and completion |
| Standard component output | Core `FlowOutput`; native serial action block | Waiting capacity plus one active delivery; output owns acceptance/delivery cancellation |
| Direct graph routing | `FlowRouter` configures that existing output | No additional standard-output queue; topology freezes before startup |
| Custom native output | `FlowRouter` creates a Core output adapter | `linkCapacity` includes active work; graph owns adapter/link cleanup |
| Stable update input | `ApplicationInputPortCore` native action block | Configured capacity includes active target admission; generation/pause/drain policy stays in host |
| Prepared revision output | `FlowValuePreparedApplicationOutput` native buffer | Configured output capacity per prepared attachment; revision owns activation/drain/detachment |
| Stable update output | `ApplicationOutputPort` configures a Core `FlowOutput` | Configured capacity includes active capture/dispatch; host prepares selected routes and completion notifications |

The update path has more queues. Sharing an implementation does not erase their
latency, memory cost or per-generation ownership; those remain performance and
retention acceptance concerns. The tables distinguish per-instance capacity from
an application-wide bound. No claim is made that multiple generations have one
combined queue capacity.

Both `FlowOutput.DeliverAsync` and `DispatchPreparedAsync` reach its single
`DeliverRoutesAsync`, which calls `FlowDeliveryBatch.SendAsync`. Native/ordinary
routes and revision-selected routes implement delivery at their appropriate
target boundary. The host no longer owns a separate batch fanout loop. Both graph
and host complete route selection through `FlowRouteSelection`.

For hosted output, `PrepareDispatchAsync` holds the revision gate while awaiting
capture and selecting a route snapshot. It releases that gate before Core awaits
selected target admissions. Held messages keep their selected route objects;
later messages select the new snapshot. Post-admission receive/observation occurs
after the Core batch returns, not when capture merely succeeds. A capture that
ignores cancellation cannot authorize delivery after abort.

Direct `FlowExecution` and `ApplicationRuntimeRevisionCandidate` both use Core
`FlowLifetime`. The latter separately owns staged stable-port drain barriers,
resource snapshots and output-failure observation. Those are real optional
transactional responsibilities; adding a nominal unused Core application would
not remove them or improve the execution model.

Direct application requests and update-host SendAndReceive use `FlowRequestReply`,
which owns one bounded `PendingExchangeCoordinator` implementation and fresh
correlation-plus-reply-address keys. Typed responses are projections afterward.
Protocol request/reply also uses that service. Retry and protocol-ack trackers
instantiate the same generic coordinator for their different state; they are not
another implementation of application first-reply matching. This audit does not
claim one global coordinator instance across independent applications.

## Concrete remaining mismatch: implicit Fluent discard

`FlowGraphBuilder.Build` currently calls DiscardUnmatched on **every** output in
`_outputs.Values.Except(_linkedOutputs)`. This covers unconnected source/branch
outputs, not only a sink deliberately selected with To. Therefore direct Fluent
can silently discard work where the ordinary Core/document path would fault.

The approved plan section 6 and implementation prompt Linking and routing require
visible unmatched failure unless discard/fallback is explicit. The current Fluent
README documents the broad behavior, but that is not evidence of an approved
exception. The passing linear/branch sample exercises connected outputs and does
not detect this mismatch. This is a source-backed finding; no new red/green
regression was added in this audit.

Next implementation: distinguish intentional terminal disposal from an accidentally
unlinked output, provide readable explicit discard intent where needed, and verify
missing-link failure plus terminal/tap/branch behavior without removing supported
pipeline shapes. Do not add another routing implementation. Review current tests
and samples before choosing the smallest public surface change.

## Verification and evidence limits

Fresh selected tests: **231 passed**, zero failed/skipped across four reports:
Core 67 per target framework; Composition 31 and Engine 66 on net10.0.
Commands and hashed reports are in
`artifacts/core-refactoring/runtime-owner-audit-20260911/verification.json`.
Selected tests include actual assertions for:

- `Standard_graph_routes_share_component_output_completion` and
  `Native_source_adapter_capacity_includes_the_active_delivery`.
- `Contract_graph_executes_with_owned_resources_borrowed_host_and_native_ports`.
- `Revision_fanout_joins_every_started_route_before_reporting_failure` and
  `Revision_fanout_keeps_captured_routes_until_delivery_settles`.
- `Held_output_dispatch_retains_its_selected_policy`, output-capacity/drain/capture
  cancellation tests, and staged/fallback revision lifetime failures.
- `Hosted_output_failure_releases_producer_and_faults_revision` and
  `Disposed_revision_does_not_observe_later_failure_of_shared_output`.

The tests ran against the unchanged qualified implementation. This turn changes
only documentation and audit artifacts; no production C# or package metadata.
Core, Engine and Fluent README statements now distinguish shared dispatch from
optional staging, and the tracing description excludes restoring stopped activity.
The frozen t1400 feed still contains the older README text; do not overwrite it.
A later candidate must incorporate documentation updates and subsequent fixes.

This closes the targeted execution-owner trace, not the full capability ledger,
all provider/protocol behavior, telemetry integration, sustained-load matrix,
transformation-stage simplification or optional-port retention audit. The implicit
Fluent discard mismatch is an actionable remaining implementation task. No public
release or external consumer coordination occurred.
