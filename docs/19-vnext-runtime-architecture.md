# vNext Runtime Architecture

Status: accepted direction, implemented incrementally.

This record defines the target architecture for the next major FluxFlow line.
The data foundation, canonical definition/address, link compilation, stable
port, system-signal, immutable DI provider-snapshot, transactional revision,
MQTT vertical slice, canonical FlowValue Mapping, canonical FlowContent
Payloads inspection, explicit canonical Serialization conversions, canonical
FlowValue JSON Schema Validation and Assertions, canonical projection-event
Expectations, canonical FlowValue/result Window, Correlation, and Join, and the
canonical hosted runtime assembler are implemented locally. State now uses
typed commands with FlowValue state and one
normal FlowResult output, Projections retains typed domain events while
emitting snapshots and expected failures through one normal FlowResult output,
and Metrics retains typed samples/snapshots while exposing successful, partial,
and failed aggregation outcomes through one normal FlowResult output.
Sessions now uses concise recorder, replay, and query nodes with exact
`FlowContent`, one normal `FlowResult` output, retained store-adapter contracts,
and no typed node compatibility layer or universal Errors stream.
Timers now uses concise interval, schedule, delay, throttle, and debounce nodes
with immutable `FlowValue` ticks, normal transform results, deterministic
clocks, and no typed compatibility layer or universal Errors stream.
Observability now uses FlowValue-native Counter, Logger, and Metrics components
with one normal result stream per component while retaining generic standalone
compatibility surfaces.
Sources now uses concise generated and deterministic sequence nodes that emit
canonical FlowValue messages with natural zero-input lifecycle semantics,
deterministic clocks, bounded fan-out, and no typed compatibility layer or
universal Errors stream.
Resources and Secrets now use canonical nested application resource addresses,
require explicit host/resource-revision/external ownership metadata, and expose
provider-owned versus non-owning keyed registration APIs. Configuration validates
the same addresses and ownership declarations.
Structural Switch, Fork, and Merge routing plus Filter and When control nodes
are deprecated in favor of canonical links. The remaining component families
through Sources are migrated. Canonical Hosting integration now loads the
flat application definition, coordinates initial and subsequent
complete-definition revisions through explicit candidate factories, preserves
active revisions on rejection, and reports source-load failures as degraded
host results without adding an Engine dependency. Designer persistence remains
the final implementation milestone.

## Package Ownership

- `FluxFlow.Data` owns transport-neutral values, content, and result contracts.
- `FluxFlow.Nodes` owns `FlowMessage<T>` and standalone Dataflow node plumbing.
- `FluxFlow.Composition` owns the canonical application document, address
  resolver, link normalization, compile-once conditions, and canonical static
  link validation. Runtime activation remains an Engine responsibility.
- `FluxFlow.Engine` executes compiled compositions and owns stable ports,
  direct port interaction, runtime revisions, system events, and diagnostics.
- `FluxFlow.Composition.Hosting` owns definition sources, immutable DI provider
  snapshots, hosted lifecycle, and Engine-independent transactional update
  coordination.
- Component runtime packages remain usable without Composition or Engine.
- Concrete adapter packages translate public contracts to private client
  library types and own those library-specific lifetimes.

The existing public definition models in Composition and Engine overlap. The
new flat definition is introduced in Composition. Engine's duplicate
definition model is removed only in an Engine major release, after a legacy
reader exists and the canonical Composition model is proven.

## Runtime Invariants

- TPL Dataflow remains the internal push-processing mechanism.
- Input capacity is finite. A full or unavailable target rejects new work as a
  normal runtime outcome rather than allowing unbounded memory growth.
- Outputs fan out to every matching link. One failed target does not stop its
  siblings.
- A shared input is never completed by one individual upstream link.
- Port addresses and subscriptions remain stable while component revisions
  attach and detach behind them.
- Messages accepted by an old revision finish there. Messages still in the
  stable mailbox are dispatched to the active revision.
- Ordinary component, resource, workflow, and link failures do not terminate
  the host. The application can remain running in a degraded state.
- Expected operation failures are data on the normal output, not a universal
  error port.

## Definition Boundary

The canonical document has exactly `Resources` and `Workflows` at the root.
Workflow objects directly contain components. Resource groups are namespace
objects without `Type`; resource leaves require `Type`. Component settings,
resource references, and port links are flat properties.

`FluxFlow.Composition.Model` now implements this document boundary, and
`FluxFlow.Composition.Addressing.ApplicationAddress` implements the shared
ordinal, case-sensitive address value for local workflow ports, absolute
workflow components and ports, nested resources, and system streams. Engine
stable-port APIs and Hosting keyed DI use the same value. Designer persistence
adopts the same canonical representation. Names containing dots are invalid.

Links may be declared once on either an input or output property as a string,
an array, or an object containing exact `Port` and optional `Condition` names.
Metadata determines direction. `ApplicationLinkCompiler` normalizes both forms
into absolute source/target links, preserves declaration side, compiles each
condition string once per activation, and validates exact types, duplicates,
explicit single-link claims, and cycles. Engine-owned system streams supply
their payload metadata to the compiler rather than creating an Engine
dependency in Composition.

## Stable Port Runtime

`FluxFlow.Engine.Ports` owns additive address-stable input mailboxes and output
broadcast hubs. Hosts register exact payload types with
`ApplicationPortRuntimeBuilder`, attach component
`ITargetBlock<FlowMessage<T>>` and `ISourceBlock<FlowMessage<T>>` instances
behind those addresses, and activate `CompiledApplicationLink` values without
reparsing definitions or recompiling conditions.

Input intake reports accepted, full, unavailable, and completed states without
waiting for component capacity. During replacement, the dispatcher pauses,
allows a message already claimed by the old target to finish, swaps the target,
then sends stable-mailbox work to the new target. A stale attachment lease
cannot detach a newer revision. Rejected target delivery retains the claimed
message for a later attachment.

Outputs broadcast each message to all compiled links, one-shot receives, and
bounded observations. Link conditions receive `input`, `payload`, and `message`
variables. Condition exceptions, full or unavailable targets, source faults,
and overflowing observations are isolated from siblings and reported through a
bounded best-effort rejection stream. Source completion only detaches that
source; it never completes the stable output or shared downstream input.

`SendAsync`, `ReceiveAsync`, `ObserveAsync`, and `SendAndReceiveAsync` use the
same canonical addresses. Receive and observation do not steal workflow data;
request/reply installs a `TraceId` waiter before input acceptance. Expected
availability and timeout states are result values while caller cancellation is
still cancellation. The rejection stream remains a bounded low-level audit
surface beneath the canonical event and diagnostic streams.

## System Events, Diagnostics, And Status

The stable runtime automatically owns the two reserved canonical outputs.
`System.Events.Output` carries `ApplicationSystemEvent` and
`System.Diagnostics.Output` carries `ApplicationDiagnostic`; both remain normal
`FlowMessage<T>` streams and use the same compiled-link and direct-access paths
as component outputs.

System-event publication is bounded, ordered, and backpressured. Accepted
events are drained during normal completion before the system output closes.
Link-condition, target, and component source/target faults are mapped to
workflow-friendly events without exposing exceptions in the payload. A failure
originating from a system stream is not recursively republished into that same
stream.

Diagnostics are bounded and best effort. Input acceptance, output emission,
request timing, rejected delivery, and system-event subscriber failure produce
diagnostic records; overflow rejects immediately without blocking workflow
processing. Accepted records integrate with `ILogger`, `ActivitySource`,
`Meter`, and `DiagnosticSource`, and host-provider exceptions are contained.

`ApplicationRuntimeStatus` snapshots runtime state and per-port availability,
pending count, and active attachment count. It does not introduce a State port.
An unexpected component source or target fault marks only that attachment
unavailable and leaves the runtime active. Revision phases use the reliable
system stream through the shared `IApplicationRevisionEventSink` boundary.

## DI Provider Snapshots

`FluxFlow.Composition.Hosting.Snapshots` builds immutable ownership boundaries
from explicitly composed `IServiceCollection` instances. Host,
resource-revision, and workflow-revision snapshots expose stable metadata for
later system events. Build and scope validation are enabled by default; scopes
are available but never created per message implicitly.

Canonical `ApplicationAddress.Value` strings are keyed-service identities.
Resources, `Workflow.Component` blocks, typed input/output ports, and
payload-independent signal targets register explicitly through normal
`IServiceCollection` extensions. Component and port views avoid duplicate
ownership while preserving normal Dataflow interfaces.

Factory-created services are provider-owned. External instances and host
providers cross the boundary only through methods explicitly named
`AddExternal...`, `BridgeExternal...`, or `CreateExternalHost(...)`, and remain
externally owned. Methods containing `View` create non-owning aliases of another
provider-owned service. Snapshots do not scan assemblies, reflect over
component types, merge providers, or fall back to arbitrary providers.

Provider snapshots remain construction and ownership primitives. Transactional
coordination composes them through candidate metadata without merging or
mutating built providers.

## Runtime Updates

`ApplicationRevisionPlanner` compares complete canonical definitions, reports
resource/workflow changes, expands transitive resource dependents, and rejects
missing references or dependency cycles. `ApplicationRevisionCoordinator`
serializes full-definition updates and delegates package-specific preparation,
activation, draining, and disposal through explicit candidates.

`ApplicationPortRuntime` stages replacement outputs behind bounded buffers,
waits for already-claimed input work, pauses only changed input/output
dispatchers, and swaps one immutable complete-link snapshot. Queued mailbox
work then reaches the new target. Failure or cancellation before candidate
activation disposes prepared work and leaves the active definition unchanged;
after activation the new immutable snapshot remains current while every old
drain/disposal action is attempted.

Port addresses and exact payload types remain stable within one runtime
generation. Same-surface revisions reuse that generation. A definition that
adds, removes, or retypes a component port prepares an isolated generation and
atomically publishes it after activation; the old generation remains alive
until its candidate drains. Direct-port callers reacquire the current runtime
through `IApplicationRuntimeAccess` after a surface-changing update. Automatic
payload migration and mapper insertion remain deferred.

`FluxFlow.Engine.Hosting.ApplicationRuntimeAssembler` is the standard concrete
candidate factory for this boundary. It consumes explicit Composition node
registrations and DI service contributors, builds candidate-owned resource and
workflow providers, validates typed component descriptors without reflection,
and stages one complete stable-port/link revision. The first activation adopts
a direct-port generation. Later revisions either reuse it for an exact surface
match or atomically replace it for a surface change while resources,
components, attachments, and routing remain transactional.

Revision port attachments and compiled links activate before source lifecycle
startup. A source that emits synchronously from `StartAsync` therefore enters
the prepared revision routes instead of racing link activation.

Standard DI remains the activation and ownership mechanism. Packages register
explicitly through `IServiceCollection`; no assembly scanning, reflection
discovery, arbitrary provider merging, or parallel registration framework is
introduced.

## MQTT Core Vertical Slice

`FluxFlow.Components.Mqtt` 5.x owns resolved broker/client settings, neutral
transport SPI contracts, multi-command request/result families, desired
subscriptions, reconnect policy, trigger claims, and standalone control,
publish, trigger, and client-event components. `FlowContent` carries MQTT
payloads; expected operation failures are polymorphic results on normal output.

One host-lifetime `MqttClientController` owns one transport session for one
logical client. Multiple controllers may share one broker endpoint while
retaining independent identity, credentials, connection state, subscriptions,
and reconnect behavior. Lifecycle and subscription mutation are serialized;
publish and status may run concurrently. Explicit Disconnect suppresses
reconnect until Connect or host restart, and availability-only auto-connect
failure does not reject controller startup.

Named subscriptions are client-owned; trigger inline subscriptions are
trigger-owned. Missing named subscriptions wait for later creation. Desired
subscriptions are restored after reconnect. Trigger claims are exclusive by
identity and identical resolved filter, while overlapping filters remain valid.
One received publication is emitted once per trigger with every matching
subscription label.

Workflow Ack/Nak signals are payload-independent and match `TraceId`. Broker
acknowledgement is separately Automatic, AfterHandoff, or AfterOutcome and is
validated against neutral adapter capabilities. Client lifecycle and
subscription events use the explicit `mqtt.events` domain stream; component
activity remains diagnostics and there is no universal State or Error port.

The existing 4.x declarations remain temporarily available. Concrete adapter
SPI implementations, shared conformance tests, and canonical Composition
resource/node binding are complete locally.

## Delivery Sequence

1. Data, envelope identity, and result contracts. Complete locally.
2. Canonical Composition definitions and addressing. Complete locally.
3. Link normalization and condition compilation. Complete locally.
4. Stable ports and direct send/receive/observe APIs. Complete locally.
5. Fault isolation, system events, and diagnostics. Complete locally.
6. Immutable DI resource/provider snapshots and canonical keyed registration.
   Complete locally.
7. Transactional resource and workflow revisions. Complete locally.
8. MQTT core resource/component vertical slice. Complete locally.
9. Concrete MQTT adapters and canonical MQTT Composition binding. Complete
   locally.
10. Component-family migrations through Sources, resource/configuration
    alignment, canonical Hosting and Designer persistence, and coordinated
    package validation. Complete locally.
11. Canonical runtime assembly from JSON through resources, components, links,
    direct stable ports, and complete-definition revision replacement. Complete
    locally.
12. Dynamic application port generations for component add, remove, and port
    type changes with drain-safe retirement. Complete locally.

Supervision, polling or latest-value APIs, durable mailboxes, broker clusters,
automatic mapper insertion, custom containers, and cyclic graphs remain
explicitly deferred.
