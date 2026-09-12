# Instance-first application checkpoint

Date: 2026-09-08. Status: executable slice verified; framework migration and release preparation remain in progress.

The direct-activation checkpoint adds three optional `BuildApplicationAsync`
overloads accepting application services, returning the same `FlowApplication`
from a setup, portable definition or authoring builder. Existing component/resource
contracts are reused; direct instance construction is unchanged. Resource
registration and component validation live in Composition and are shared through
public boundaries by the optional update path. Revision-specific snapshots stay
in Engine, and Composition grants it no production friend access. Core adds only
internal lifetime/identity plumbing and no dependency-injection package. The
composition sample no longer references Engine. The source baseline records the
new overloads and resource-registration entry point. Its lexical counts include public
members of internal types; they are not a binary compatibility claim. See the
execution ledger for final qualification and outstanding gates.

The canonical-declaration checkpoint (2026-09-09) now uses sealed native
ComponentInputPort/ComponentOutputPort declarations and flat HasInput/HasOutput
bindings across optional authoring and all 19 family registration files in scope.
Typed physical hierarchies and unused port lifecycle helpers are removed. Typed
caller projections, shapes, events, control ownership and direct instance-first
construction remain. Qualification: 3950 selected tests, 252 release checks,
one local workflow acceptance, clean non-incremental build. Full performance and
fresh isolated package qualification remain open; see the execution ledger.

Later execution has consolidated foundation/coordination ownership into Core,
migrated routing/evaluator/observability components, and changed the pipeline
fluent path to direct Core registration without its old host dependency. The
initial migration list below is historical; the execution ledger records which
items are closed and the remaining delivery, authoring and operations gates.

The 2026-09-09 direct-port checkpoint additionally makes all six serialization
nodes, file read/write, storage put/get/query/delete and session recorder/query
direct IFlowComponent instances. Their port inventory is forwarded from existing
pipelines; supplied stores stay borrowed. This does not yet cover every special
wrapper or source, and does not add the deferred context-aware construction API.

The subsequent timer checkpoint migrates delay/throttle/debounce onto the shared
FlowNode lifecycle and exposes direct source ports for interval/schedule. Their
generic operator variants and duplicate timer pipeline are removed. Pure timing
and error forwarding preserve the original message identity. Other special
wrappers and authoring/operations convergence remain tracked in the ledger.

## Direct construction

The subsequent source/bridge migration gives directory enumeration and file
watching the shared FlowSource lifecycle. The inbound request bridge and trigger
are direct source/control instances with canonical Output/Responses and explicit
startup. Trigger conversion queues and the duplicate filesystem source pipeline
are removed. A later inbound-exchange checkpoint moves bridge matching onto Core's
fresh exchange correlation plus reply route. Transport IDs stay on contexts, and
the trigger's duplicate source buffer is removed. The execution ledger records
qualification. The subsequent tracker retirement removes the unused callback
wrapper and sweep option; explicit keyed settlement stays in Core and automatic
transport notifications stay in the bridge. Other delivery/authoring/operations
gates remain open.

The retry checkpoint now exposes one canonical FlowRetryNode as IFlowComponent.
Input/Ack/Nak/Cancel are the actual bounded native message blocks. Feedback can
produce output, so these are ordinary business inputs rather than terminal controls.
Direct and optional document construction use that same class. The copied-input
queue and duplicate typed/canonical wrappers are removed. Native SendAsync reports
admission; result matching remains visible through Output/Events. The unchanged
cycle guard still applies. Final checkpoint qualification is tracked in the ledger.

Terminal-control bindings now take the actual ITargetBlock<FlowMessage>, including
keyed DI resolution. The custom signal-target interface, signal envelopes and
separate hosted input/route are removed. One canonical input dispatcher serves
both kinds while retaining non-owning terminal-control lifetime. Typed caller
convenience converts at the edge. Native kind/identity, backpressure, cancellation,
revision handover/rollback and disposal are verified in the execution ledger.

## Direct construction example

The working direct path accepts the actual component instance. Naming supplies a graph address; it does not invoke a creator, resolve a service, or manufacture an activation descriptor.

```csharp
await using var app = new FlowApplication();
var main = app.AddWorkflow("main");
var reports = app.AddWorkflow("reports");

var source = main.Add("source", new Sequence(messages, harness));
var transform = main.Add("double", new Double(dependency));
var left = reports.Add("left", new Sink());
var reply = reports.Add("reply", new Reply());
var errors = reports.Add("errors", new Sink());

source.Output.LinkTo(transform.Input);
left.Input.LinkFrom(transform.Output, message => !message.IsError);
transform.Output.LinkTo(reply.Input, message => !message.IsError);
transform.Output.LinkTo(errors.Input, message => message.IsError);
app.RepliesFrom(reply.Output);

await app.StartAsync();
var response = await app.RequestAsync(
    app.GetInput("main.double.Input"), reply.Output,
    FlowMessage.Create(FlowValue.From(99)), TimeSpan.FromSeconds(10));
```

Sequence, Double, Sink and Reply above are the compiled test components, not new mandatory framework abstractions. The [complete executable example](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/tests/Unit/Authoring/FluxFlow.Json.Tests/DocumentKernelParityTests.cs) includes their implementations, source gating, final drain and assertions. Existing canonical FlowNode subclasses inherit the local port declaration and can be added directly. Other component families still require their planned migration.

An independently authored sink implements IFlowComponent with a FlowPorts inventory over its actual input block and the usual Complete/Fault/Completion/DisposeAsync operations. It needs no output placeholder, event placeholder, DI registration or base-class inheritance. Source and standard input/output bases expose their real ports automatically. A component-owned static factory returning an IFlowComponent is equally usable by Add; the graph performs no special activation step.

Successful Add transfers disposal ownership by default. ComponentOwnership.Borrowed retains external disposal ownership while the application still stops and drains the instance. Failed Add does not transfer ownership. Reusing a stateful instance in another graph is rejected. Shared constructor dependencies remain the component author's explicit borrowed dependencies, not graph-created services. The optional context-aware Add overload remains deferred.

## What actually executes

```text
direct instances ──────────────────────────────┐
portable fluent → existing portable definition ├→ Core named graph
JSON → existing portable definition → bindings┘       │
                                                     ▼
component input block → component processing → bounded routing block
                                                 ├→ selected target A
                                                 └→ selected target B
                                                      │
reply output → existing bounded exchange coordinator ←┘
```

JSON uses a trusted type-id-to-constructor function at the document boundary. For example, `bindings.Add<SourceOptions>("sample.source", options => new Sequence(options.Messages, harness))`. This is optional authoring configuration; ordinary Core Add does not call it. The Core project references neither the document model nor Fluent, Designer, expression implementations, transport clients, hosting or revisions.

The document and fluent examples in the same test file use the existing flat Resources/Workflows/Type/Input/Output representation. Workflow expression selection overrides the application default. Host-native evaluator configuration supplies a function; portable Parameters supplies its per-condition value. Two equal conditions share one compilation, and each message is evaluated with its own values. Export of fluent links explicitly projects them into the same document representation; native delegates are rejected instead of silently omitted.

## Verified behavior

| Requirement | Executed evidence |
|---|---|
| Same real scenario through constructors, JSON and portable fluent | Direct_constructors_execute_the_complete_core_scenario; Json_executes_the_same_core_scenario_with_scoped_compiled_conditions; Portable_fluent_executes_the_same_core_scenario_without_component_creators |
| Portable links survive export; native delegates cannot disappear | Portable_links_survive_export_and_execute_on_the_same_kernel; Native_fluent_predicates_cannot_be_silently_exported |
| Constructor-once activation and pre-start failure cleanup | Unknown_document_type_is_rejected_before_any_constructor_runs; Invalid_document_port_disposes_constructed_components_without_starting_sources |
| Real fan-in, routing, reference fanout and ownership | InstanceFirstGraphTests, nine cases |
| Unique exchange keys, expected reply route, timeout including admission, cancellation, capacity and shutdown | RequestReplyTests, six cases |
| Partial startup cleanup and no resource disposal while processing is active | FlowExecutionTests, three cases |

The initial slice's focused Core run passed 18 cases; document/parity run passed seven. All 25 were subsequently discovered and passed at solution level. That full unit filter executed 2,978 cases with two release-governance failures. Subsequent project consolidation and fixture migration resolved those failures; the nullable event projections were also corrected without suppressions. The native-dispatch checkpoint passed **2,982 unit cases and one separate workflow acceptance case**, with zero warnings/errors in its nonincremental solution build. Exact artifacts and later scoped results are in the execution ledger. These are checkpoint results, not a release-completion claim.

## Intentional unfinished migration work

The bullets below record the temporary state at the API checkpoint. The subsequent project-consolidation step moved Nodes/Coordination source into Core and replaced those two project/package entries with Core and Json. Both now have unpublished alpha development metadata. Core no longer references either retired assembly. The delivery, authoring and capability deletion gates below still apply; project consolidation alone is not completion.

- Core currently references the old Nodes and Coordination assemblies while using their real canonical block execution and the existing exchange coordinator. Their approved destination is Core; the legacy typed runtime has not yet been deleted.
- The routing block currently follows the existing standalone FlowOutput queue. The additional buffering is internal, temporary scaffolding. Consolidate it only after preserving admission-time snapshots, slow-target behavior, unlink, MaxMessages and completion contracts. No throughput/allocation improvement is claimed at this checkpoint.
- Json temporarily references Composition to reuse its existing document model, strict link parser and condition compiler. Its temporary friend access must disappear when those files move. It does not call the old component catalog or activation factories.
- The portable fluent entry executes Core, but the old Flow.From implementation and the Fluent assembly's old Engine dependency remain. They are not hidden behind the new direct Core API and must be removed in authoring/runtime migration.
- The new Core and Json projects are deliberately not packable yet. Fluent's temporary reference to Json means the package train is not currently a releasable candidate. Do not waive package-governance checks to conceal this state.
- The new JSON path explicitly rejects resource documents and legacy activation descriptors for now. Their existing implementations are preserved, and their migration remains required. This slice is not a replacement for all currently supported documents yet.
- Dynamic link transitions, events aggregation/addresses, standard instrumentation, optional updates, durability, all module families, complete evaluator parity and package-only consumer verification remain required. Existing capabilities have not been declared removed or migrated merely because this slice passes.
- Container-backed checks remain unavailable because the configured engine pipe is absent. No integration or performance acceptance claim follows from these unit tests.

Nothing has been published, pushed, tagged, or sent to a consumer task. Continue with the approved runtime and authoring consolidation; do not mark the overall goal complete at this checkpoint.
