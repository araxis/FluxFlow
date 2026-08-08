# Goal: Complete the typed code-first path and remove the remaining parallel authoring/runtime seams

- Date: 2026-08-08
- State: complete
- Scope: FluxFlow Composition, Engine, durable input/output integration, application resources, MQTT Composition, Fluent and Fluent.Hosting, samples, package acceptance, public API governance, documentation, and memory
- Compatibility: deliberate breaking changes are allowed; do not retain obsolete aliases merely for backward compatibility

## Objective

Make compiled-C# FluxFlow applications feel like one coherent, explicit system from declaration through execution:

1. typed component handles created during application authoring remain useful when the application is running;
2. code-first resource declarations carry their executable resource behavior so a second package/resource registration is not required;
3. `FluxFlow.Fluent` keeps its concise `From`/`Then`/`Tap`/`Branch`/`Apply` syntax but executes through the canonical `ApplicationDefinition`, `ComponentContract`, and `FluxFlowApplication` path instead of constructing a second live runtime model;
4. raw/dynamic component registration remains possible, but it is visibly advanced and no longer competes with normal `ComponentContract` authoring;
5. JSON/configuration applications remain portable, serializable, hot-reloadable definitions and continue to register executable package behavior explicitly.

The result must be powerful but lightweight: direct C#, small explicit objects, ordinary dependency injection, deterministic ownership, no reflection, no assembly scanning, no global registries, and no hidden convention-based activation.

## User intent

The normal code-first experience should read as a single declaration and execution path:

```csharp
var application = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var workflow);

workflow
    .AddComponent("source", SampleComponents.Source, out var source)
    .AddComponent("processor", SampleComponents.Processor, out var processor)
    .AddComponent("sink", SampleComponents.Sink, out var sink);

source.Output.ConnectTo(processor.Input);
processor.Output.ConnectTo(sink.Input);

var definition = application.Build();

services.AddFluxFlow(definition);
```

The same typed handles should work at the host boundary:

```csharp
await fluxFlow.Ports.SendAsync(
    processor.Input,
    FlowMessage.Create(value));

var output = await fluxFlow.Ports.ReceiveAsync(
    sink.Output,
    timeout: TimeSpan.FromSeconds(10));
```

For code-first MQTT, adding MQTT resources to the definition must be sufficient to make their registrar available:

```csharp
var application = new ApplicationDefinitionBuilder()
    .AddResourceGroup("messaging", out var messaging)
    .AddWorkflow("main", out var workflow);

messaging
    .AddMqttBroker("broker", options, out var broker)
    .AddMqttClient("client", clientOptions, out var client);

services.AddFluxFlow(application.Build());
```

The code-first path above must not require a duplicate `AddFluxFlowComponents().AddMqtt()` call solely to supply the resource registrar or descriptors already captured by the definition.

## Architectural principles

- KISS: prefer small immutable contracts and thin delegating overloads.
- SRP: authoring describes the application; contracts describe executable behavior; Engine activates and owns revisions; DI supplies host dependencies.
- IOC/DIP: component and resource factories receive explicit contexts and use ordinary DI. Do not introduce service locators or static mutable registries.
- Exact identity: executable C# behavior is identified by the exact contract/descriptor/registrar object, not delegate hashing, structural guessing, reflection, or generated IDs.
- Atomic builders: a failed option callback, property projection, handle factory, contract conflict, or validation must not partially mutate the application builder.
- Clear ownership: host-owned services are never disposed by revision snapshots; revision-owned resources and components are disposed exactly once and in deterministic order.
- Flat authoring: normal samples should remain at one fluent level, with at most one component/resource-specific options callback.
- Independent JSON: portable JSON and executable C# are parallel sources for the same Engine, not serializers for one another.
- No feature loss: preserve hot reload, revision rollback, typed predicates, durability, MQTT lifecycle, Fluent fan-out/fan-in/segments, events, completion, and hosting.

## Current problems

### 1. Typed handles stop at the runtime boundary

`ApplicationDefinitionBuilder` produces `InputPortHandle<T>`, `SignalInputPortHandle`, and `OutputPortHandle<T>`, but `ApplicationPorts`, `ApplicationPortRuntime`, durable input enqueue, and durable output capture primarily accept strings or `ApplicationAddress`. Fully code-first callers therefore return to string/address plumbing after authoring.

### 2. Resources still use split declaration and execution registration

Code-first MQTT declares broker, retry, subscription, and client resources in the application definition, then separately calls `AddMqtt()` so Engine can discover `MqttCompositionResourceRegistrar`. This is the same split source-of-truth problem that `ComponentContract` removed for components.

Host-owned external resources also require callers to manually reconstruct address keys or write an `IApplicationResourceRegistrar`, even when a typed `ResourceHandle<T>` already exists.

### 3. Fluent is a separate execution model

`FluxFlow.Fluent` currently creates and directly links node instances, then runs them through `ApplicationRuntime.Create`. Canonical code-first applications use `ApplicationDefinitionBuilder`, `ComponentContract`, stable application ports, revisions, resource snapshots, and `FluxFlowApplication`. The two models duplicate lifecycle, completion, hosting, event aggregation, and user concepts.

### 4. Dynamic registration is too prominent

`AddRuntimeComponent` sits beside the normal registration APIs even though complete `ComponentContract` declarations are now the intended path. The raw path remains necessary for dynamic plugins and externally selected types, but its current name and placement make it look like a second normal authoring model.

## Required final public shape

Exact names may be adjusted only when the implementation proves a name ambiguous, but the semantic surface must remain this small.

### A. Typed runtime port overloads

Add typed overloads that delegate to the existing address-based implementation without duplicating runtime logic:

```csharp
ValueTask<PortSendResult> SendAsync<T>(
    InputPortHandle<T> input,
    FlowMessage<T> message,
    CancellationToken cancellationToken = default);

ValueTask<PortSendResult> SendAsync<T>(
    SignalInputPortHandle input,
    FlowMessage<T> message,
    CancellationToken cancellationToken = default);

Task<PortReceiveResult<T>> ReceiveAsync<T>(
    OutputPortHandle<T> output,
    TimeSpan? timeout = null,
    CancellationToken cancellationToken = default);

ValueTask<PortObserveResult<T>> ObserveAsync<T>(
    OutputPortHandle<T> output,
    int capacity = 128,
    CancellationToken cancellationToken = default);

Task<PortRequestResult<TResponse>> SendAndReceiveAsync<TRequest, TResponse>(
    InputPortHandle<TRequest> input,
    OutputPortHandle<TResponse> output,
    FlowMessage<TRequest> request,
    TimeSpan? timeout = null,
    CancellationToken cancellationToken = default);
```

Add corresponding typed overloads to the low-level runtime attachment surface where semantically valid:

- `AttachInputAsync(InputPortHandle<T>, ...)`;
- `AttachSignalInputAsync(SignalInputPortHandle, ...)`;
- `GetSignalTarget(SignalInputPortHandle)`;
- `AttachOutput(OutputPortHandle<T>, ...)`.

Do not add payload-only overloads that silently discard or invent message metadata. `FlowMessage<T>` remains explicit.

Keep all existing string and `ApplicationAddress` overloads for JSON, dynamic selection, remote/operational tooling, and advanced hosting.

### B. Typed durability overloads

Add only the overloads whose port direction and payload type make semantic sense:

```csharp
ValueTask<DurableInputEnqueueResult> EnqueueAsync<T>(
    InputPortHandle<T> input,
    FlowMessage<T> message,
    CancellationToken cancellationToken = default);

DurableOutputRegistrationBuilder Capture<T>(
    OutputPortHandle<T> output,
    string contractName,
    JsonTypeInfo<T> jsonTypeInfo);
```

They must delegate to existing address-based behavior and retain all validation, identity, retention, leasing, delivery, and error semantics.

### C. Typed host resource registration

Add `ResourceHandle<T>` overloads to the existing explicit DI helpers:

```csharp
IServiceCollection AddFluxFlowResource<TService>(
    ResourceHandle<TService> resource,
    Func<IServiceProvider, TService> factory);

IServiceCollection AddExternalFluxFlowResource<TService>(
    ResourceHandle<TService> resource,
    TService service);
```

These overloads use `resource.Address` and remain ordinary keyed DI registration. They must not introduce ownership inference: `AddExternalFluxFlowResource` remains non-owning from the revision snapshot's perspective, while ordinary DI owns services it constructs according to standard DI rules.

### D. Executable application resource contracts

Introduce a small `ApplicationResourceContract` family analogous to `ComponentContract`:

```csharp
public abstract class ApplicationResourceContract
{
    public string Type { get; }
}

public class ApplicationResourceContract<THandle> : ApplicationResourceContract
    where THandle : AuthoredResourceHandle;

public class ApplicationResourceContract<TOptions, THandle> : ApplicationResourceContract
    where TOptions : class
    where THandle : AuthoredResourceHandle;
```

A contract owns:

- one normalized portable resource `Type`;
- the explicit option-builder factory when options are needed;
- the explicit option-to-`ResourceDefinitionBuilder` projection;
- the typed handle factory;
- the exact `IApplicationResourceRegistrar` responsible for executing resources of that contract/family.

The contract must not own a serialized delegate representation, global registration, service-provider instance, or mutable runtime resource.

Add `AuthoredResourceHandle` as the controlled base for package-specific handles, mirroring `AuthoredComponentHandle`. Existing `ResourceHandle<T>` remains the general typed handle.

`IResourceDefinitionContainerBuilder`, `ApplicationDefinitionBuilder`, and `ResourceGroupBuilder` must support contract-based resource creation with return-handle and fluent `out var` forms. Normal package extensions continue to provide flat domain names such as `AddMqttBroker`; callers should not normally invoke generic resource-contract APIs directly.

Resource addition must be atomic:

1. validate name and contract;
2. construct options;
3. run the caller's options callback;
4. project properties into a temporary definition builder;
5. validate the portable `ResourceInstanceDefinition`;
6. create the typed handle;
7. validate contract compatibility against contracts already captured by the application;
8. commit the resource entry and contract together.

Any exception before the final commit leaves both resource entries and captured contracts unchanged.

### E. Definition-owned resource contracts

`ApplicationDefinition` gains a read-only runtime-only collection of exact application resource contracts. This collection:

- is populated only by compiled-C# contract authoring;
- is empty for the public portable constructor and JSON deserialization;
- is excluded from JSON serialization;
- is copied defensively and ordered deterministically;
- deduplicates reuse of the exact same contract reference;
- rejects different contracts claiming the same resource type;
- never serializes registrars, factories, delegates, handles, or CLR types.

Canonical JSON must remain exactly `Resources` plus `Workflows`; first-class code-only links and executable component/resource contracts remain excluded.

### F. Engine registrar merge and revision semantics

For every candidate revision, Engine computes one effective resource-registrar set from:

- host-registered `IApplicationResourceRegistrar` instances; and
- registrars carried by definition-owned application resource contracts.

Rules:

- exact registrar reference reuse is idempotent;
- deterministic ordering is required;
- no reflection or registrar-type scanning;
- different contracts for the same resource type conflict before activation;
- code-first definition registrars are candidate-revision inputs, not mutations of the root service collection;
- JSON definitions have no embedded registrars and therefore continue to rely on explicit package registration such as `.AddMqtt()`;
- a failed candidate does not alter the active revision or dispose its resources;
- successful replacement disposes the retired revision-owned resources exactly once;
- host-owned fallback services are never disposed by Engine;
- changing an exact resource contract while keeping identical portable properties must still be visible to revision planning for resources of that type and their dependent workflows;
- removed code-first contracts and captured registrar closures become collectible after successful retirement.

### G. MQTT migration

Expose one complete contract for each MQTT application resource type:

- broker;
- retry policy;
- subscription;
- client.

The resource contracts may share the exact same stateless MQTT registrar instance. `AddMqtt()` must register that exact registrar for JSON/configuration hosts and register the official component contracts, preserving Designer metadata and all MQTT resource validation.

The code-first MQTT authoring extensions must add these contracts to the definition. A code-first application that uses only embedded MQTT component/resource contracts must run with:

```csharp
services.AddFluxFlow(definition);
```

It must not require a second `.AddMqtt()` call. A JSON/configuration application must still call `.AddMqtt()` explicitly.

External MQTT controller resources remain explicitly host supplied, but should use the typed `AddExternalFluxFlowResource(resourceHandle, controller)` helper instead of a custom sample registrar.

Preserve:

- logical client ownership and controller lifetimes;
- broker, retry, subscription, and client validation;
- keyed resource addresses;
- reconnect behavior;
- acknowledgement behavior;
- subscriptions and publish behavior;
- configuration conversion;
- Designer metadata;
- exact error diagnostics.

### H. Fluent consolidation

Retain the concise public authoring features:

- `Flow.From`;
- `Then`;
- `Tap`;
- `Branch`;
- shared-node fan-in;
- `FlowSegment` and `Apply`;
- `To`;
- `OnEvent`;
- `StartAsync`, `StopAsync`, `Completion`, and `DisposeAsync`;
- Generic Host integration.

Replace the internal direct-link/runtime path:

- `FlowGraphBuilder` must build one canonical `ApplicationDefinitionBuilder` and one workflow;
- every unique node instance becomes one explicit instance-backed component contract with deterministic internal component/type/port names;
- main inputs and outputs become descriptor ports and typed definition links;
- shared node references map to one component, preserving fan-in;
- `Tap` maps to an additional canonical link while retaining the main continuation;
- arbitrary branch output blocks are adapted through a small explicit internal non-owning port-source component when they are not already a known canonical output; do not use reflection to discover a property owner;
- node event streams are attached explicitly to one owned aggregate so `FlowGraph.Events` and `OnEvent` retain their current `FlowEvent` behavior;
- the resulting `FlowGraph` wraps an owned `FluxFlowApplication` and owned service-provider scope, not `ApplicationRuntime.Create`;
- `FlowGraph` exposes the canonical `ApplicationDefinition` and `FluxFlowApplication` for inspection instead of exposing the old parallel `ApplicationRuntime`;
- start activates the canonical application revision;
- stop completes entry nodes, waits for graph drain, and stops the application;
- disposal is idempotent, releases subscriptions, disposes the canonical application/provider once, and never double-disposes shared nodes;
- no hot-reload promise is added to instance-backed Fluent graphs; the canonical application object is used for lifecycle and inspection, while already-constructed node instances remain single-use.

After migration, `FluxFlow.Fluent` must no longer call `ApplicationRuntime.Create`, manually own direct Dataflow links, or maintain a second completion/lifecycle algorithm that competes with Engine.

`FluxFlow.Fluent.Hosting` may remain as a compatibility feature package, but it must host the canonical-backed `FlowGraph`; it must not restore a separate runtime.

### I. Advanced dynamic registration

Keep dynamic/plugin registration through an explicit advanced entry point, for example:

```csharp
services.AddFluxFlowComponents()
    .Advanced
    .AddDynamicComponent("dynamic.transform", component => { ... });
```

Required behavior:

- introduce a small `AdvancedFluxFlowRegistrationBuilder` owned by `FluxFlowRegistrationBuilder`;
- expose it through a read-only `Advanced` property;
- move/rename `AddRuntimeComponent` to `AddDynamicComponent` on the advanced builder;
- remove the old normal-surface `AddRuntimeComponent` API and migrate repository-owned callers;
- keep `RuntimeComponentRegistrationBuilder` because complete contracts use it to describe factories and ports;
- keep `UseInstanceFactory` as the explicit low-level binding escape hatch used by Fluent's instance adapter and truly dynamic registrations;
- do not create obsolete forwarding aliases;
- normal samples and getting-started docs must not mention the advanced path except in a clearly labeled advanced section.

### J. Public API and overload discipline

- Do not remove the user-approved `Connect` and `ConnectTo` alternatives.
- Do not merge JSON string conditions with C# delegates.
- Do not add callback-style `AddWorkflow` nesting.
- Do not introduce payload-only send shortcuts in this goal.
- Do not proliferate duplicate overloads beyond typed handle/address/string boundaries with distinct purposes.
- Keep package-specific options and handles package-specific.
- Prefer immutable state and init-only/record options where already established; do not force mutable `IOptions<T>` semantics into application definitions.

## Explicit non-goals

- Serializing C# predicates, factories, registrars, contracts, handles, or node instances.
- Reconstructing executable contracts when JSON is deserialized.
- Assembly scanning, reflection-based discovery, source-generated global registration, or static mutable registries.
- Automatically installing packages or discovering MQTT from resource type strings.
- Moving backend, MQTT, durability, or resource settings into `FluxFlowApplicationOptions`.
- Changing canonical address syntax or portable JSON property names.
- Replacing `FlowMessage<T>` with raw payloads.
- Changing durable delivery guarantees, schemas, lease semantics, retention, or storage providers.
- Changing component processing profiles or port cardinality semantics.
- Publishing packages, creating a release, pushing a branch, or creating a pull request.

## Implementation phases

### Phase 1: typed runtime and durability handles

1. Add typed overloads to `ApplicationPorts`.
2. Add typed attachment/signal/output overloads to `ApplicationPortRuntime` only where the low-level API already has the equivalent address operation.
3. Add typed durable input enqueue and durable output capture overloads.
4. Add typed resource DI overloads.
5. Delegate all behavior to existing address methods.
6. Migrate code-first samples/docs to retain and use typed handles.
7. Keep address and string regression tests.

### Phase 2: executable application resource contracts

1. Add `AuthoredResourceHandle` and the contract types.
2. Add a shared application-level contract collection.
3. Extend resource containers with atomic contract-based additions.
4. Capture contracts in `ApplicationDefinition`.
5. Keep JSON projection unchanged.
6. Merge embedded and host registrars per revision.
7. Update revision planning for resource-contract identity.
8. Prove rollback, retirement, ownership, and collectible closure behavior.
9. Add typed host external-resource registration.

### Phase 3: MQTT contract migration

1. Create the four complete MQTT resource contracts.
2. Reuse one exact stateless registrar.
3. Update all MQTT resource authoring extensions.
4. Make `.AddMqtt()` the explicit JSON/configuration registration path.
5. Remove duplicate `.AddMqtt()` from the code-first sample path.
6. Replace the sample registrar for external client binding with typed DI registration.
7. Verify configuration and code-first outputs remain equivalent.

### Phase 4: Fluent canonicalization

1. Add the Engine dependency required by the canonical runner.
2. Replace direct node/link accumulation with canonical component registrations and definition links.
3. Preserve deterministic node identity and shared-node fan-in.
4. Add the explicit branch-port adapter.
5. Preserve FlowEvent aggregation without reflection.
6. Rebuild `FlowGraph` around `FluxFlowApplication` and an owned provider.
7. Remove the `ApplicationRuntime` exposure and direct-create path.
8. Keep Fluent.Hosting behavior over the canonical-backed graph.
9. Update samples and package acceptance.

### Phase 5: advanced API separation

1. Introduce the advanced registration builder.
2. Rename/move raw registration to `AddDynamicComponent`.
3. Migrate dynamic fixtures, tests, and advanced documentation.
4. Add public-surface guards preventing `AddRuntimeComponent` from returning.

### Phase 6: governance and documentation

Update at minimum:

- root `README.md`;
- Composition, Engine, Fluent, Fluent.Hosting, and MQTT READMEs;
- getting started;
- definitions and links;
- hosting and observability;
- durable input/output documentation;
- public API overview;
- typed code-first authoring;
- unified component contracts;
- canonical migration guidance;
- release validation;
- docs index;
- package-consumer acceptance source/script assertions;
- public API baseline;
- `memory/00-index.md`;
- `memory/01-current-state.md`;
- a new memory record for this goal.

Document the two intentionally independent paths:

| Source | Portable definition | Embedded executable contracts | Required host registration |
|---|---:|---:|---|
| Compiled C# | Yes | Component and resource contracts used by the builder | Ordinary host dependencies only |
| JSON/configuration | Yes | None | Explicit component/resource family registration |

## Required tests and evidence

Use existing xUnit and Shouldly conventions. The dedicated testing agent owns test implementation and `.testagent` artifacts.

### Typed ports

- typed send reaches the same input and returns the same result as address send;
- typed signal send preserves signal behavior;
- typed receive/observe/request-reply use exact generic payload types;
- typed attach input/signal/output delegates to the same runtime port;
- null handles, unavailable revisions, completion, cancellation, timeout, and wrong-address regression behavior remain deterministic;
- string and `ApplicationAddress` overloads remain present and green.

### Durability

- typed durable enqueue preserves message identity and address key;
- typed durable capture preserves exact output address, contract identity, and JSON type info;
- input handles cannot be used for capture and output handles cannot be used for enqueue at compile time/source-surface level;
- all existing provider conformance suites remain green.

### Resource contracts

- construction validates type, factories, projections, handles, and registrar;
- exact contract reuse deduplicates;
- distinct contracts for the same type conflict;
- nested resource groups share one contract collection;
- failed options/projection/handle/conflict operations are atomic;
- JSON omits contracts and deserialization produces none;
- portable JSON round-trips unchanged;
- Engine executes embedded registrars without host registration;
- exact host+definition registrar reuse is idempotent;
- candidate conflicts fail before active revision mutation;
- add/remove/replace contract revisions are detected;
- failed replacement retains the active resource generation;
- successful replacement disposes retired revision resources and releases captured closures;
- host fallback resources are not disposed by Engine.

### MQTT

- all four resource authoring extensions capture the expected exact contract;
- code-first MQTT runs without `.AddMqtt()`;
- JSON/configuration MQTT still requires and succeeds with `.AddMqtt()`;
- exact mixed registration deduplicates;
- external typed resource registration resolves by the resource handle address;
- broker/retry/subscription/client validation remains unchanged;
- MQTT configuration and code-first sample outputs remain equivalent.

### Fluent

- linear pipeline behavior remains identical;
- `Tap` fan-out preserves the main continuation;
- arbitrary branch ports work without reflection;
- shared node fan-in completes only after all upstreams;
- reusable segments build fresh nodes per graph;
- null/invalid builder guards remain;
- events are observed from the first active message and throwing handlers remain isolated;
- completion faults for node failures;
- start/stop/dispose are idempotent and dispose each shared node once;
- `FlowGraph.Definition` contains the canonical workflow, components, links, and descriptors;
- `FlowGraph.Application` is the canonical application;
- source-shape tests prove `ApplicationRuntime.Create` and manual Dataflow linking are absent from `FluxFlow.Fluent`;
- Fluent.Hosting still builds, starts, stops, and supports multiple graphs.

### Advanced surface

- normal `FluxFlowRegistrationBuilder` exposes `Advanced`;
- `AddDynamicComponent` exists only on the advanced builder;
- `AddRuntimeComponent` is absent from public source/baseline;
- dynamic factory, raw instance binding, validation, cleanup, and duplicate behavior remain green;
- normal samples contain no advanced registration.

### Package and repository gates

- package-only consumer exercises typed runtime handles, embedded resource contracts, canonical-backed Fluent, JSON explicit registration, durability restart, and advanced dynamic registration where appropriate;
- exact package closure remains correct;
- all public API baseline and family/convention tests pass;
- focused project tests pass with zero warnings;
- full Release solution build passes with zero warnings and errors;
- full solution tests pass with no failures or skips;
- dedicated Release tests pass;
- `dotnet format --verify-no-changes` passes;
- `git diff --check` passes;
- vulnerability audit reports no vulnerable direct or transitive packages;
- scans find no reflection/scanning/global registry, obsolete `AddRuntimeComponent`, normal-sample advanced APIs, TODO/FIXME, or skipped tests in the touched slice.

## Acceptance criteria

The goal is complete only when all of the following are true:

1. A code-first caller can author components and use the same typed handles for runtime and durability operations without reconstructing addresses.
2. A code-first MQTT definition runs without a duplicate `.AddMqtt()` registration.
3. A JSON/configuration MQTT application remains portable and succeeds through explicit `.AddMqtt()` registration.
4. Resource contract addition and Engine activation are atomic, revision-aware, deterministic, and lifecycle-correct.
5. `FluxFlow.Fluent` retains its user-visible composition features but no longer creates `ApplicationRuntime` or manually owns a second direct-link runtime.
6. Dynamic registration remains available through an obviously advanced surface, while the old normal `AddRuntimeComponent` API is removed.
7. No reflection, assembly scanning, delegate serialization, global mutable registry, or hidden package discovery is introduced.
8. Existing component contracts, JSON, hot reload, durability, MQTT, Designer metadata, and package behavior remain intact.
9. Samples, documentation, public API baseline, goal evidence, and memory describe the final implementation accurately.
10. Every required focused and repository-wide verification gate is green.

## Completion evidence

Completed on 2026-08-08.

### Implemented boundaries

- Typed runtime operations were added in `ApplicationPorts` and
  `ApplicationPortRuntime`; durable input/output and resource DI helpers now
  accept the matching typed handles and delegate to the existing address-based
  implementations.
- `ApplicationResourceContract`, `ApplicationResourceContractCollection`, and
  `AuthoredResourceHandle` make compiled-C# resources executable definition
  inputs. `ApplicationDefinition.ApplicationResourceContracts` is immutable,
  runtime-only, and excluded from portable JSON.
- Engine merges explicit host registrars and definition-owned registrars by
  exact registration identity for each candidate revision. Planning, rollback,
  replacement, disposal, host fallback, and captured-closure retirement are
  covered without changing JSON semantics.
- MQTT exposes four resource contracts backed by one stateless registrar.
  Code-first MQTT runs from `AddFluxFlow(definition)`; configuration-driven
  MQTT retains explicit `.AddMqtt()` registration. The sample demonstrates and
  executes both paths.
- `FluxFlow.Fluent` now compiles graphs to a canonical
  `ApplicationDefinition` and runs an owned `FluxFlowApplication`. Its prior
  parallel `ApplicationRuntime.Create` path is absent while linear flows,
  branching, fan-in, taps, segments, events, errors, completion, and hosting
  remain available.
- A deterministic topological drain plan preserves finite-source delivery in
  acyclic canonical graphs; cyclic graphs keep the coordinated fallback.
- Raw registration moved to
  `AddFluxFlowComponents().Advanced.AddDynamicComponent(...)`; the normal
  `AddRuntimeComponent` surface and forwarding aliases were removed.
- Samples, package acceptance, public API baseline, documentation, docs index,
  current-state memory, and this decision record describe the final two-path
  model.

### Requirement evidence

| Requirement | Evidence |
|---|---|
| Typed send, signal, receive, observe, request/reply, and attachment | `ApplicationPortHandleTests.Typed_handles_send_receive_observe_and_request_through_their_exact_addresses`; `Typed_handle_attachments_bind_message_signal_and_output_to_exact_runtime_ports`; complete Engine suite |
| Typed durable enqueue and capture | `DurableApplicationInputsTests.Typed_input_handle_enqueue_uses_exact_address_contract_and_cancellation`; `DurableOutputRegistrationTests.Typed_output_handle_capture_uses_exact_address_type_and_atomic_conflict_rules` |
| Atomic, exact-identity resource authoring and JSON omission | `TypedCodeFirstResourceAuthoringTests` and `ApplicationDefinitionJsonTests.Code_first_resource_contracts_are_omitted_from_json_and_deserialization_is_contract_free` |
| Resource execution, revision rollback, ownership, and retirement | `ApplicationResourceContractRuntimeTests`, including code-first activation, exact registrar dedupe, replace/remove, failure rollback, disposal, and closure retirement |
| MQTT code-first without duplicate registration; JSON explicit registration | `MqttServiceCollectionExtensionsTests.Code_first_definition_embeds_mqtt_components_and_resources_without_AddMqtt`; existing configuration registration tests; successful MQTT sample execution through both paths |
| Canonical Fluent behavior without a parallel runtime | `FlowBuilderTests`, `FlowObservationTests`, `FlowGraphHostingTests`, and Release source-governance facts; 21 Fluent and 5 Fluent.Hosting focused tests passed |
| Advanced-only dynamic registration | `FluxFlowRegistrationBuilderTests`, `RuntimeComponentBindingBuilderTests`, Engine raw-instance validation, and Release public/source surface assertions |
| Official package/family completeness | Release 19-family/44-contract convention and matrix tests; accepted public API baseline |
| Package-only execution and cleanup | `PackageConsumerAcceptanceScriptTests.Acceptance_script_pack_mode_cleans_owned_source_and_workdir_after_success`; every JSON, code-first, resource, Fluent, durability, seed/recovery, and completion marker occurred exactly once, all 14 expected child invocations ran, and both owned temporary directories were removed |

### Verification results

| Gate | Result |
|---|---|
| Focused Composition | 160 passed, 0 failed/skipped, 0 warnings |
| Focused Engine | 130 passed, 0 failed/skipped, 0 warnings; deterministic replacement lifetime pair 2/2 |
| Focused DurableInput / DurableOutput | 157 / 181 passed, 0 warnings |
| Focused Fluent / Fluent.Hosting | 21 / 5 passed, 0 warnings |
| Focused MQTT Composition | 23 passed, 0 warnings |
| Focused Release governance, docs, source, and package facts | 41 + 5 + 2 passed, 0 warnings |
| Public API baseline | 2 passed after intentional baseline acceptance; stable in the full Release run |
| Real isolated package-consumer pack gate | 1 passed, 0 warnings; exact markers/invocations and cleanup verified |
| MQTT sample | Both configuration and code-first definitions published the two expected messages |
| `dotnet build FluxFlow.sln -c Release --no-restore --nologo` | 134 projects, 0 errors, 0 warnings |
| `dotnet test FluxFlow.sln -c Release --no-build --no-restore --nologo` | 2,627 passed in 66 projects, 0 failed, 0 skipped, 0 warnings |
| Dedicated `FluxFlow.Release.Tests` | 179 passed, 0 warnings |
| `dotnet format FluxFlow.sln --verify-no-changes --no-restore --severity warn` | Passed |
| Vulnerability audit, direct and transitive | No vulnerable packages in any solution project |
| `git diff --check` | Passed |

The final source scan found no current normal-surface
`AddRuntimeComponent`, no `ApplicationRuntime.Create` use in
`FluxFlow.Fluent`, no reflection/scanning/global executable registry in the new
authoring path, and no new TODO/FIXME, skipped test, or arbitrary sleep in the
touched slice. The one `ComponentAuthoringContract` documentation occurrence is
intentional migration text naming the removed API.
