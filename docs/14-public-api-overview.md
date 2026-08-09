# Public API Overview

FluxFlow is organized as independently versioned packages. Runtime component
packages expose standalone Dataflow nodes. Composition packages add canonical
JSON registration and Designer metadata. Hosts own concrete clients, stores,
clocks, expression engines, credentials, and other resources.

## Foundation

### FluxFlow.Nodes 4.x

- `FlowContent` in the `FluxFlow.Data` namespace: exact owned bytes plus optional
  content type and encoding.
- `FlowError` in the `FluxFlow.Data` namespace: transport-neutral processing
  failure with stable code, category, transient flag, and detached optional JSON
  details.
- `FlowMessage<T>`: one active value or `FlowError` plus trace, message,
  causation, optional correlation, timestamp, and immutable string headers.
  `FlowMessage.Restore(...)` and `RestoreError(...)` are the explicit
  invariant-safe boundary for reconstructing persisted identity.
- `FlowOutput<T>`: bounded live fan-out with awaitable acceptance, reliable
  in-process normal-data delivery, graceful drain, and no replay.
- `FlowNode<TInput,TOutput>` and `FlowSource<TOutput>`: standalone Dataflow
  foundations with reliable normal Output, best-effort Events, Completion, and
  async disposal. `FlowNodeOptions` and `FlowSourceOptions` own instance-level
  capacities.
- `IFlowNode`, `IFlowSource`, `FlowEvent`, and typed identifier contracts.

Errors travel on normal outputs. `Events` is diagnostics; `Completion` reports
lifecycle completion or an unrecoverable block fault.
The `FluxFlow.Data` namespace is retained for source compatibility, while its
types now live in the `FluxFlow.Nodes` assembly and package. The former
`FluxFlow.Data` package is retired without a forwarding assembly or type
forwarders. See [Flow Data Contracts](20-flow-data-contracts.md).

### FluxFlow.Mapping 1.x

Defines `IFlowExpressionEngine`, compiled expressions, typed mapper/predicate
interfaces, delegate/expression implementations, and `FlowMapContext`. It is an
engine-free abstraction and does not own a dynamic workflow representation.

### FluxFlow.Coordination 2.x and FluxFlow.Resilience 1.x

Coordination supplies bounded generic pending exchanges with deterministic
timeouts and exact-once settlement. Resilience supplies transport-neutral retry
policies, schedules, budgets, jitter, and state transitions. Components keep
their own protocol classification and lifecycle ownership.

## Application Runtime

### FluxFlow.Composition 6.x

Owns the canonical application model (`Resources` and `Workflows`), component
definitions, addresses, validation, exact canonical registration, flat C#
authoring builders, typed component contracts and handles, first-class
in-memory links, link
compilation, processing profiles, code-first runtime ownership, and component
event fan-in. Links may be declared at either endpoint and compile into one
canonical model. Signal feedback is explicit; ordinary data cycles remain
invalid. Official composition packages expose `<Family>Components` contracts
and component-specific flat builders on top of the same generic
`ApplicationDefinitionBuilder`; both delegate to one add core and do not own
host resource lifecycles.
`ComponentContract<THandle>` and `ComponentContract<TOptions,THandle>` combine
the runtime descriptor, typed handle, and optional authoring options in one
explicit declaration. `ApplicationDefinitionBuilder.Build()` retains the exact
descriptors used by compiled C# components, so `AddFluxFlow(definition)` can
activate them without duplicate service registration. JSON definitions keep
their independent explicit host-registration boundary.
`ApplicationResourceContract<THandle>` and
`ApplicationResourceContract<TOptions,THandle>` apply the same rule to
resources: one portable type/options projection, typed handle, and explicit
registrar. The built definition retains exact code-first contracts in the
runtime-only `ApplicationResourceContracts` collection; JSON omits them.
Component-family builders expose package-owned `BoundedCapacity` or
domain-specific capacity names and persist those values in the canonical
definition; an omitted value keeps the package default.
The application, resource-group, and workflow builders support both
handle-returning declarations and chain-first declarations with a final
`out var` handle. Chain-first overloads return the same parent instance, and
`Connect(...)` returns its application or workflow builder. `ConnectTo` returns
the same output handle for typed fan-out. Insertion order never implies a
connection, default port, or payload conversion; both workflow-capture styles
build the same directly executable immutable `ApplicationDefinition`.
Synchronous typed predicates are definition-owned and require no expression
engine; portable expression strings retain their existing engine path.
Compiled C# and portable JSON are independent authoring sources that converge
at validation and compilation, not serialization. Designer remains JSON-only.
`ApplicationLinkCompilationResult.Declarations` exposes resolved,
immutable persistence facts, and `ApplicationLinkCompiler.SerializeDeclarations`
writes the same canonical grammar. Resource registrars and canonical keyed DI
registration helpers are low-level public extension contracts shared by Engine
and composition adapters; no production friend assembly is required.

### FluxFlow.Engine 7.x

Owns `FluxFlowApplication`, definition sources, hosted lifecycle, transactional
revision activation, stable `ApplicationPorts`, system events, and diagnostics.
A candidate revision is prepared in isolation and becomes active atomically;
failed preparation leaves the prior revision running.
Each candidate uses one effective component catalog formed from host-registered
and definition-owned descriptors. Exact descriptor reuse is accepted; a
different descriptor for the same type fails before activation. Revision-owned
resources override ordinary host-service fallback during component activation,
and Engine never owns the fallback host provider.
Engine likewise forms one deterministic effective registrar set from host and
definition resource contracts. Exact registrar identity reuse is idempotent;
conflicts fail before the active revision changes. `ApplicationPorts` accepts
typed input/signal/output handles for send, receive, observe, and request/reply,
while preserving string and `ApplicationAddress` operations.
`FluxFlowApplicationOptions.InputCapacity` and `OutputCapacity` configure only
the stable application-port layer and do not override component definitions or
standalone node options.

### FluxFlow.Engine.HealthChecks 1.x

Adds the optional `IHealthChecksBuilder.AddFluxFlowApplication()` adapter. It
registers one idempotent standard check named `fluxflow.application` with the
fixed `fluxflow` and `ready` tags. The internal check translates existing
`FluxFlowApplication` state, active revision, and last-update status into
healthy, degraded, or unhealthy readiness with at most seven bounded metadata
fields. It adds no Engine reverse dependency, public options, background work,
I/O, endpoint, or external dependency probe.

### FluxFlow.Engine.DurableInput 1.x

Adds an optional provider-neutral inbox in front of Engine message inputs.
`DurableApplicationInputs` persists exact message identity through a host-owned
`IDurableInputStore`; a bounded sequential hosted dispatcher uses explicit
typed contract registrations and lease-token compare-and-set transitions for
at-least-once delivery. `EngineAccepted` remains the default acknowledgement
mode. Hosts may explicitly select `WorkflowCompleted`, supply exactly one
`IDurableInputCompletionSource`, and keep the current lease alive through one
provider-owned `IDurableInputLeaseRenewalStore`; that mode dispatches one entry
at a time and settles only an explicit completion result for the exact lease.
Engine does not reference this package, and no graph, output, trace, or timing
signal is inferred as completion. Concrete stores remain separate packages.
Providers may additionally expose
`IDurableInputDeadLetterStore` for bounded metadata listing, exact inspection,
and generation-protected explicit replay without changing the delivery store.
`IDurableInputStatusStore` is a separate optional payload-free snapshot of
pending, lease, terminal, and dead-letter counts at a caller-supplied time.
`IDurableInputRetentionStore` separately performs explicit address-scoped,
bounded deletion of old delivered tombstones or dead letters. Purging a
delivered identity ends its deduplication window.
Code-first callers may enqueue through `InputPortHandle<T>`; the overload
delegates to the same address-based store path and does not add a signal-input
shortcut.

### FluxFlow.Engine.DurableInput.SqlFile 1.x

Adds the production local SQLite implementation of `IDurableInputStore` with a
flat `AddFluxFlowSqlFileDurableInput(...)` registration, immutable provider
options, lazy transactional schema initialization, deterministic atomic lease
batches, token-based transitions, transactional schema-1-to-2 migration, and
the optional dead-letter operations capability. The same singleton also
implements exact token- and expiry-protected lease renewal for workflow-
completion acknowledgement and exposes read-only operational status without
initializing schema. Version 1.3 adds transactional terminal retention without
changing schema version 2 or Engine.

### FluxFlow.Engine.DurableInput.TSql 1.x

Adds the production networked implementation of `IDurableInputStore`,
`IDurableInputDeadLetterStore`, and `IDurableInputLeaseRenewalStore`. One flat
`AddFluxFlowTSqlDurableInput(...)` callback creates immutable redacted options
and one exact singleton alias set without database work during registration or
resolution. Direct parameterized SQL provides serializable idempotent enqueue,
locking-read-committed multi-host leases, exact token transitions and renewal,
bounded dead-letter operations, and generation-protected replay. Schema
creation or validation is explicit; Engine, workflow definitions, and
`FluxFlowApplicationOptions` remain unchanged. Version 1.1 adds the same
payload-free status capability as an alias of that singleton. Version 1.2 adds
bounded terminal retention through the same singleton and existing schema.

### FluxFlow.Engine.DurableOutput 3.x

Adds optional provider-neutral capture of explicitly selected application
outputs before Engine dispatches them to links or live host taps. A flat
`AddFluxFlowDurableOutput(...)` builder binds canonical output addresses to
stable contract names and explicit `JsonTypeInfo<T>` metadata. The host supplies
one `IDurableOutputStore`. Code-first callers may pass
`OutputPortHandle<T>` directly; it delegates to the same address registration
and has no input/signal shortcut. Hosts can independently enable a small serial hosted
dispatcher through `AddFluxFlowDurableOutputDelivery(...)`, one
`IDurableOutputDeliveryStore`, and one `IDurableOutputDeliveryHandler`. Leasing,
exact renewal for long-running handlers, completion, fixed retry, and optional
final-attempt dead-letter settlement provide at-least-once delivery without
enlarging the capture-store interface or
changing Engine. `IDurableOutputDeadLetterStore` separately provides bounded
metadata listing, exact lookup, and generation-protected explicit replay. No
transport is included. `IDurableOutputStatusStore` is a separate optional
payload-free snapshot that distinguishes unmaterialized captures from tracked
delivery state. `IDurableOutputRetentionStore` separately deletes old completed
or dead-lettered capture parents in bounded transactions; the delivery rows are
removed through the existing cascade. Version 2.0 was breaking for custom delivery stores because
the cohesive delivery interface gained `DeadLetterAsync(...)`; version 2.1
adds status without changing existing store interfaces. Version 2.2 adds the
separate retention capability without enlarging those interfaces. Version 3.0
adds `RenewLeaseAsync(...)` to the cohesive delivery interface and requires a
flat positive renewal interval shorter than the lease duration.

### FluxFlow.Engine.DurableOutput.SqlFile 3.x

Adds the production local SQLite implementation of `IDurableOutputStore` with
flat `AddFluxFlowSqlFileDurableOutput(...)` registration, immutable provider
options, lazy version-1 capture schema initialization, exact envelope persistence, and
atomic `Enqueued`/`AlreadyExists`/`Conflict` behavior. The same singleton also
implements both later capabilities using a separate lazy version-2
lease/tombstone/dead-letter schema with transactional v1 migration. Capture-only
hosts never touch that schema, including during read-only status inspection.
Version 2.1 exposes status as a fourth alias of the same singleton and does not
change Engine or output declarations. Version 2.2 adds explicit bounded
terminal retention without a new schema version.
Version 3.0 adds direct transactional exact-token renewal without changing the
version-2 delivery schema.

### FluxFlow.Engine.DurableOutput.TSql 2.x

Adds the production networked T-SQL implementation of the capture, delivery,
dead-letter, status, and retention capabilities. Flat
`AddFluxFlowTSqlDurableOutput(...)` registration resolves
a temporary builder into an immutable record and aliases one store singleton.
The provider uses direct parameterized SQL, an explicit versioned schema under
a bounded application lock, locking read-committed leases, and atomic
compare-and-set settlement/replay. Registration is side-effect-free, and the
provider remains independent of Engine application options and local SQLite.
Version 1.1 adds payload-free status as a singleton alias without schema repair,
a transaction, or a worker. Version 1.2 adds bounded transactional retention
through that same singleton.
Version 2.0 adds direct parameterized exact-token renewal without a schema
change, ORM, worker, or additional dependency.

### FluxFlow.Fluent 4.x

Builds concise typed code-first graphs over the canonical Composition and Engine
contracts. Unique node instances become instance-backed component contracts;
typed `Then`, `Tap`, `Branch`, fan-in, and segment connections become canonical
definition links. `FlowGraph.Definition` exposes the immutable application and
`FlowGraph.Application` exposes the owned `FluxFlowApplication`. There is no
parallel Fluent runtime or direct manual Dataflow-link lifecycle. Fluent event
observation aggregates explicit node event streams; expected processing errors
remain normal value-or-error messages.

## Component Contracts

The following table lists the maintained runtime surface. Every output type is
inside `FlowMessage<T>` and may instead carry `FlowError`.

| Family | Primary nodes | Value contracts |
|--------|---------------|-----------------|
| Mapping | `FlowMapperNode<TInput,TOutput>`, `JsonMapperNode` | typed T input/output; explicit JSON specialization |
| Assertions | `AssertionNode<T>`, `JsonAssertionNode` | `T` -> `AssertionResult<T>` |
| Validation | `JsonSchemaValidatorNode` | `JsonElement` -> `JsonSchemaValidationResult` |
| Routing | `WindowNode<T>`, `CorrelationNode<T>`, `JoinNode<TLeft,TRight>` plus JSON specializations | typed windows and correlation/join outcomes |
| State | `StateReducerNode<T>`, `JsonStateReducerNode` | `StateReducerInput<T>` -> `StateReducerResult<T>` |
| Sources | `GeneratedSourceNode<T>`, `SequenceSourceNode` | `T` or `SequenceItem` |
| Timers | interval/schedule sources and generic delay/throttle/debounce nodes | typed ticks or pass-through T |
| FileSystem | read/write transforms and directory/watch sources | exact content plus `DirectoryEntry`/`FileChange` |
| Serialization | JSON, text, and Base64 conversion nodes | explicit `FlowContent`, `JsonElement`, and string conversions |
| Payloads | `PayloadInspectNode` | `FlowContent` -> `PayloadInspectionResult` |
| Observability | generic counter/logger/metrics nodes plus JSON specializations | typed input -> snapshot/log records |
| Metrics | `MetricsAggregateNode` | `MetricSampleInput` -> `MetricSnapshotOutput` |
| Projections | `EventProjectionNode` | `ProjectionEvent` -> `EventProjectionSnapshot` |
| Expectations | `EventExpectationNode` | `ProjectionEvent` -> `EventExpectationResult` |
| HTTP | `HttpClientNode` | `HttpClientRequest` -> `HttpResponseResult` |
| Storage | put/get/delete/query nodes | typed request and outcome contracts over host stores |
| Sessions | recorder/replay/query nodes | typed content records and query outcomes |
| Resilience | `FlowRetryNode<T>` | T attempts, signal inputs, and typed retry outcomes |
| MQTT | control, publish, receive, and events nodes | typed client requests/results and exact received content |

Expected business variants remain in these result contracts. Processing
failures set `FlowMessage.IsError`; they are not wrapped in another result type.
Projection event, filter, summary, and snapshot attribute maps are defensive
read-only snapshots with ordinal key semantics. Mutating a source dictionary
after contract initialization cannot change projection or expectation behavior.

## Mapping, JSON, and Expressions

Typed components keep CLR values typed. Configuration-driven mapping,
assertion, routing, state, and validation registrations use explicit
`JsonElement` specializations where their document contract is schema-less.
Expression engines adapt those values according to their own language. A mapper
may intentionally emit a CLR record, dictionary, or `ExpandoObject`, but no
dynamic object is required by the runtime.

## Content and Transport Boundaries

`FlowContent` preserves bytes exactly. HTTP, MQTT, FileSystem, Storage, and
Sessions use it where a raw body is the real contract. Serialization nodes make
text, JSON, and Base64 conversion visible. There is no lazy decode cache or
hidden codec lookup. Decode before broadcast to share one immutable decoded
value, or branch first to retain both raw and decoded paths.

MQTT's core controller owns logical client behavior while concrete adapter
packages own provider sessions. Broker resources own endpoint defaults; client
resources own identity, credentials, reconnect, subscriptions, and lifecycle.
Workflow acknowledgement remains separate from broker acknowledgement.

HTTP's client node uses a host-owned `HttpClient`; the ASP.NET Core adapter owns
endpoint integration and request/reply wiring. Neither moves server or client
ownership into Engine.

## Composition Adapters and Designer Metadata

Each maintained `.Composition` package registers stable component type names,
fixed typed ports, flat options, host-owned resource references, and Designer
option/resource hints. Each active family owns one `*ComponentDefinition` with
nested `Types`, `Options`, `Ports`, and `Resources`; its service extension
uses the same flat `AddComponent(...)` signature for every designed component,
and each component supplies its own options, ports, resources, and metadata in
that callback. The runtime and design catalogs are registered automatically.
There is no public declaration model, metadata-provider discovery, or reflection
scan.

Every maintained typed component `Add*` authoring method also exposes a
delegating fluent-capture overload with the same arguments followed by an
`out` parameter of its typed handle. MQTT resource authoring preserves the
concrete resource-container receiver type, so application and resource-group
chains retain their normal API surface.

The maintained inventory contains 19 active component composition packages.
The empty Control runtime and composition migration markers have been retired;
their previously published versions remain restorable for migration only and
have no replacement package.

```json
{
  "Resources": {
    "Expressions": {
      "Default": { "Type": "expression.engine" }
    }
  },
  "Workflows": {
    "Orders": {
      "Map": {
        "Type": "data.map",
        "Expression": "...",
        "Engine": "Resources.Expressions.Default",
        "Input": "Receive.Output",
        "Output": "Validate.Input"
      }
    }
  }
}
```

There are no maintained `Composition`, `Nodes`, or root `Links` wrappers.
Component and workflow names come from object keys. Resources may be nested and
use exact addresses such as `Resources.Expressions.Default`.

## Resource and Ownership Packages

Hosts own resource addressing, secret resolution, and configuration validation.
FileSystem and SQL-file Storage adapters implement the neutral store boundary;
they do not change workflow message semantics or own host lifetime.

Registration uses one predictable outer shape without forcing unrelated
settings into a universal options object:

```csharp
components.AddComponent("orders.review", component =>
{
    component.WithDisplay("Order Review", "Orders");
    component
        .UseFactory(CreateOrderReview)
        .HasInput("Input", static node => node.Input)
        .HasOutput("Output", static node => node.Output)
        .HasEvents("Events", static node => node.Events);
});

services.AddFluxFlowFileSystemStorage("items-store", storage =>
{
    storage.RootDirectory = "data/storage";
    storage.DefaultCollection = "items";
});

services.AddFluxFlowSqlFileStorage("audit-store", storage =>
{
    storage.DatabasePath = "data/audit.db";
    storage.DefaultCollection = "records";
});
```

Component families bind their own immutable options records from individual
application-definition nodes. Storage callbacks use backend-specific temporary
builders and produce immutable backend options snapshots. Custom storage and
session resources use standard exact-key DI:

```csharp
services.AddKeyedSingleton<IStorageStoreFactory>("custom-store", customFactory);
services.AddKeyedSingleton<IStorageStore>("shared-store", sharedStore);
services.AddKeyedSingleton<ISessionStoreFactory>("session-factory", sessionFactory);
services.AddKeyedSingleton<ISessionStore>("shared-sessions", sharedSessionStore);
```

Direct stores remain host-owned and have precedence over keyed factories.
Factory leases carry backend-specific ownership. `AddMqtt()` remains an
application-resource registrar: the host provides transports, credentials,
certificates, clocks, and secret policy, while each revision owns its client
controllers. None of these backend/resource settings belongs in
`FluxFlowApplicationOptions`.

## Error and Diagnostic Policy

- Normal per-message failures are `FlowError` data on Output.
- Domain-negative results stay in the declared result type when they are valid
  operation outcomes.
- Components propagate incoming errors unless explicitly error-aware.
- Events describe input, output, lifecycle, and operational diagnostics.
- Block faults are reserved for violated invariants or unrecoverable lifecycle
  failures and do not define application host lifetime.
- Links and expressions may route on `isError`, `error.code`, typed result
  properties, headers, and other message fields.

## Public API and Versioning

Package projects target supported stable frameworks and avoid preview union
syntax. The value-or-error invariant is implemented behind private construction
and can adopt a future stable language union without changing its meaning.

The public API baseline in `eng/public-api/baseline.txt` records normalized
source declarations. SDK
package validation remains the binary compatibility gate. Major versions in
this release train intentionally remove obsolete hosting, migration, registry,
and alias surfaces; no runtime fallback recreates them.


## Shipped Package Index

The manifest is authoritative for shipped package identities and project-owned versions.

| Package | Version | Composition API or role |
|---------|---------|-------------------------|
| `FluxFlow.Nodes` | `4.0.0` | node foundation plus the `FluxFlow.Data` content/error namespace |
| `FluxFlow.Coordination` | `2.0.0` | runtime or support package |
| `FluxFlow.Resilience` | `1.0.0` | runtime or support package |
| `FluxFlow.Components.Resilience` | `2.0.0` | runtime or support package |
| `FluxFlow.Components.Resilience.Composition` | `4.0.0` | `AddResilience`; `ResilienceComponentDefinition` |
| `FluxFlow.Composition` | `6.0.0` | immutable DI-backed component descriptors, exact catalog, application model, resource registrar, and runtime |
| `FluxFlow.Mapping` | `1.0.3` | runtime or support package |
| `FluxFlow.Components.RequestReply` | `2.0.0` | runtime or support package |
| `FluxFlow.Components.Http.AspNetCore` | `2.0.0` | runtime or support package |
| `FluxFlow.Engine` | `7.0.0` | unified hosted application lifecycle, revisions, stable ports, and diagnostics |
| `FluxFlow.Engine.HealthChecks` | `1.0.0` | optional standard .NET readiness check over existing Engine application state |
| `FluxFlow.Components.Mqtt` | `7.1.0` | neutral TCP, TLS, WebSocket, and secure WebSocket MQTT orchestration |
| `FluxFlow.Components.Mqtt.Composition` | `7.1.0-rc.1` | `MqttComponentDefinition`; portable MQTT resources and complete code-first contracts over revision-owned controllers |
| `FluxFlow.Components.Mqtt.MqttNet` | `3.1.0` | MQTTnet transport adapter for the neutral broker modes |
| `FluxFlow.Components.Mqtt.PulseMqtt` | `4.1.0` | Pulse MQTT 2.29 raw-client adapter for the neutral broker modes |
| `FluxFlow.Components.Mapping` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Mapping.Composition` | `6.0.0` | `AddMapping`; `MappingComponentDefinition` |
| `FluxFlow.Components.Assertions` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Assertions.Composition` | `6.0.0` | `AddAssertions`; `AssertionsComponentDefinition` |
| `FluxFlow.Components.Sources` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Sources.Composition` | `6.0.0` | `AddSources`; `SourcesComponentDefinition` |
| `FluxFlow.Components.Routing` | `6.0.1` | runtime or support package |
| `FluxFlow.Components.Routing.Composition` | `6.0.0` | `AddRouting`; `RoutingComponentDefinition` |
| `FluxFlow.Components.Validation` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Validation.Composition` | `6.0.0` | `AddValidation`; `ValidationComponentDefinition` |
| `FluxFlow.Components.FileSystem` | `6.0.1` | runtime or support package |
| `FluxFlow.Components.FileSystem.Composition` | `6.0.0` | `AddFileSystem`; `FileSystemComponentDefinition` |
| `FluxFlow.Components.Observability` | `7.0.0` | runtime or support package |
| `FluxFlow.Components.Observability.Composition` | `6.0.0` | `AddObservability`; `ObservabilityComponentDefinition` |
| `FluxFlow.Components.Timers` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Timers.Composition` | `6.0.0` | `AddTimers`; `TimersComponentDefinition` |
| `FluxFlow.Components.Payloads` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Payloads.Composition` | `5.0.0` | `AddPayloads`; `PayloadsComponentDefinition` |
| `FluxFlow.Components.Http` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Http.Composition` | `6.0.0` | `AddHttp`; `HttpComponentDefinition` |
| `FluxFlow.Components.Serialization` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Serialization.Composition` | `5.0.0` | `AddSerialization`; `SerializationComponentDefinition` |
| `FluxFlow.Components.Metrics` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Metrics.Composition` | `5.0.0` | `AddMetrics`; `MetricsComponentDefinition` |
| `FluxFlow.Components.Projections` | `7.0.0` | immutable ordinal projection attribute snapshots |
| `FluxFlow.Components.Projections.Composition` | `5.0.0` | `AddProjections`; `ProjectionsComponentDefinition` |
| `FluxFlow.Components.Expectations` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Expectations.Composition` | `6.0.0` | `AddExpectations`; `ExpectationsComponentDefinition` |
| `FluxFlow.Components.Designer` | `5.0.0` | component metadata derived from the immutable component catalog |
| `FluxFlow.Components.Sessions` | `6.0.1` | session contracts and nodes; stores use standard keyed DI |
| `FluxFlow.Components.Sessions.Composition` | `6.0.0` | `AddSessions`; `SessionsComponentDefinition` |
| `FluxFlow.Components.State` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.State.Composition` | `6.0.0` | `AddState`; `StateComponentDefinition` |
| `FluxFlow.Components.Storage` | `7.0.0` | runtime contracts with immutable ordinal attribute snapshots |
| `FluxFlow.Components.Storage.Composition` | `6.0.0` | `AddStorage`; `StorageComponentDefinition` |
| `FluxFlow.Components.Storage.FileSystem` | `5.0.0` | flat `AddFluxFlowFileSystemStorage` keyed factory registration |
| `FluxFlow.Components.Storage.SqlFile` | `5.0.0` | flat `AddFluxFlowSqlFileStorage` keyed factory registration |
| `FluxFlow.Fluent` | `4.0.0` | instance-first facade producing canonical `ApplicationDefinition` graphs hosted by `FluxFlowApplication` |
| `FluxFlow.Fluent.Hosting` | `4.0.0` | Generic Host lifecycle integration for canonical-backed `FlowGraph` instances |
| `FluxFlow.Engine.DurableInput` | `1.1.0` | optional provider-neutral leased at-least-once input delivery with Engine-accepted or explicit workflow-completed acknowledgement |
| `FluxFlow.Engine.DurableInput.SqlFile` | `1.1.0` | SQLite single-file durable-input store with exact lease renewal for local hosts |
| `FluxFlow.Engine.DurableInput.TSql` | `1.0.0` | networked T-SQL durable-input store with shared leases, exact renewal, dead-letter inspection, and replay |
| `FluxFlow.Engine.DurableOutput` | `2.0.0` | optional capture, serial leased delivery, bounded attempts, and dead-letter operations |
| `FluxFlow.Engine.DurableOutput.SqlFile` | `2.0.0` | SQLite capture, schema-v2 delivery state, dead-letter inspection, and replay |
| `FluxFlow.Engine.DurableOutput.TSql` | `1.0.0` | networked T-SQL capture, shared leases, dead-letter inspection, and replay |
