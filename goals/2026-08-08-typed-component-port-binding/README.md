# Goal: Unify Component Port Declaration and Runtime Binding

## Status

- State: complete
- Date: 2026-08-08
- Repository: `C:\Projects\FluxFlow`
- Accepted base branch: `main`
- Breaking changes: allowed
- Backward-compatibility shims: not required
- Runtime behavior: preserve
- Component functionality: preserve
- Package publication: out of scope
- Commit, push, pull request, or release: do not perform unless separately authorized

## Objective

Simplify FluxFlow component registration by eliminating the duplicated declaration of input names, output names, message types, and runtime Dataflow bindings.

The current authoring model requires component authors to declare ports twice:

1. inside `ComponentInstance.Create(...)`, using `ComponentPorts.Input(...)`, `ComponentPorts.Output(...)`, and optionally `events: node.Events`; and
2. again on `RuntimeComponentRegistrationBuilder` or `ComponentRegistrationBuilder`, using `AddInput<T>(...)` and `AddOutput<T>(...)`.

Although the two declarations currently serve different internal purposes—static descriptor metadata versus activated runtime bindings—the public authoring API must make one explicit declaration authoritative for both.

Implement a typed, flat component-factory builder where:

- `UseFactory(...)` establishes the concrete node type;
- `AddInput(name, selector)` declares input metadata and binds the activated input;
- `AddSignalInput(name, selector)` declares signal metadata and binds the activated signal target;
- `AddOutput(name, selector)` declares output metadata and binds the activated output;
- `AddEvents(name, selector)` declares a named event output and binds its event source;
- message types are inferred from the selected node members wherever C# type inference permits;
- component authors never repeat the same port name or message type between descriptor and runtime construction;
- normal authoring does not directly construct `ComponentInstance`;
- normal authoring does not directly call `ComponentPorts.Input`, `Output`, or `SignalInput`;
- every port is explicit—no event port is injected automatically;
- the design remains flat, strongly typed, reflection-free, deterministic, and understandable.

Do not lose runtime validation, event conversion, lifecycle ownership, custom cleanup, processing capabilities, options, resources, Designer metadata, application linking, JSON compatibility, or existing component behavior.

## Mandatory Initial Repository Handling

Before modifying code:

1. Inspect `git status`, the active branch, and recent history.
2. Preserve every existing user change.
3. The working tree currently contains the completed package-consumer process-restart durability work. Do not discard, reset, overwrite, or accidentally rewrite it.
4. Pay particular attention to the existing changes in:
   - `eng/package-consumer-acceptance`;
   - `tests/FluxFlow.Release.Tests/PackageConsumerAcceptanceScriptTests.cs`;
   - durability documentation;
   - `memory`;
   - the existing restart-durability goal.
5. Update affected parts of those changes to the new component API when necessary, but do not change their durability behavior or acceptance contract.
6. Do not use destructive Git operations.
7. Before implementation, create:
   - `goals/2026-08-08-typed-component-port-binding/README.md`
8. Store this accepted goal in that file and maintain its status and verification evidence during execution.
9. Do not stage, commit, push, or publish unless separately authorized.

## Authoritative Public Authoring Shape

The normal runtime-only component API must support this flat form:

```csharp
services.AddFluxFlowComponents()
    .AddRuntimeComponent("sample.uppercase", component =>
    {
        component
            .UseFactory(static _ => new UppercaseNode())
            .AddInput("Input", static node => node.Input)
            .AddOutput("Output", static node => node.Output)
            .AddEvents("Events", static node => node.Events);
    });
```

The port name must appear exactly once. The `string` message type in this example must be inferred from the selected node input and output members.

A source component should read naturally:

```csharp
component
    .UseFactory(static context =>
    {
        var options = context.BindConfiguration<SourceOptions>();
        return new StringSourceNode(options.Messages);
    })
    .AddOutput("Output", static node => node.Output)
    .AddEvents("Events", static node => node.Events);
```

A sink should read naturally:

```csharp
component
    .UseFactory(context => new CollectSinkNode(collector))
    .AddInput("Input", static node => node.Input)
    .AddEvents("Events", static node => node.Events);
```

Signal inputs must use the same model:

```csharp
component
    .UseFactory(CreateTriggerNode)
    .AddSignalInput("Ack", static node => node.Ack)
    .AddSignalInput("Nak", static node => node.Nak)
    .AddOutput("Output", static node => node.Output)
    .AddEvents("Events", static node => node.Events);
```

Support synchronous and asynchronous node factories without forcing callers to write unnecessary `ValueTask.FromResult(...)` wrappers:

```csharp
component.UseFactory(static _ => new UppercaseNode());
```

```csharp
component.UseFactory(static context => CreateNodeAsync(context));
```

Use clear overloads and constraints. The activated node type must implement `IFlowNode`.

## Fluent Builder Requirements

`UseFactory(...)` must return a node-typed fluent builder. The exact internal type name may be selected during implementation, but the public behavior must be clear and cohesive.

The typed builder must provide:

```csharp
AddInput(
    string name,
    nodeSelector,
    ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
```

```csharp
AddSignalInput(
    string name,
    nodeSelector,
    ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
```

```csharp
AddOutput(
    string name,
    nodeSelector,
    ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
```

```csharp
AddEvents(
    string name,
    nodeSelector,
    ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
```

Each method must return the same typed builder so the calls remain fluent.

Port selectors must be strongly typed delegates:

- input selectors return an `ITargetBlock<FlowMessage<TMessage>>`;
- signal selectors return an `IFlowSignalTarget`;
- normal output selectors return an `ISourceBlock<FlowMessage<TMessage>>`;
- event selectors return an `ISourceBlock<FlowEvent>`.

Do not use:

- reflection;
- expression-tree inspection;
- property-name conventions;
- runtime type scanning;
- attributes for port discovery;
- assembly discovery;
- dynamic dispatch;
- `object`-based selector contracts;
- service-location tricks;
- factory execution during registration;
- nested port callbacks;
- terminal `Build`, `Commit`, or `Apply` calls.

The component configuration callback must still execute synchronously and exactly once during registration.

## One Authoritative Port Declaration

Each typed port call must create both parts of the component contract.

At registration time it must create immutable descriptor metadata:

- normalized port name;
- message type;
- signal versus message semantics;
- link cardinality;
- input or output direction;
- Designer metadata where applicable.

At activation time it must use the stored selector to create the corresponding runtime binding against the activated node.

Conceptually:

```csharp
.AddInput("Input", static node => node.Input)
```

must generate both:

```csharp
ComponentPortMetadata.Create<string>("Input")
```

and:

```csharp
ComponentPorts.Input<string>("Input", node.Input)
```

The component author must not write those two forms independently.

Selectors must not execute during registration. They execute only after the node factory successfully returns a node instance.

## Explicit Event Ports

Events must behave as explicitly declared named ports.

Use:

```csharp
.AddEvents("Events", static node => node.Events)
```

or:

```csharp
.AddEvents("Diagnostics", static node => node.Events)
```

The author chooses the port name. `"Events"` is a normal explicit choice, not an automatically injected or globally reserved name.

Required event behavior:

- Remove automatic event-output insertion from `ComponentDescriptor`.
- Remove the assumption that every component has an output named `"Events"`.
- Remove the global reservation that prevents a normal output from using the name `"Events"`.
- A component that does not call `AddEvents(...)` must not receive an implicit event output.
- `AddEvents(...)` creates output metadata whose externally visible payload type is `ComponentEvent`.
- The selected node source continues to produce `FlowEvent`.
- Preserve the existing bridge that converts `FlowEvent` into addressable `ComponentEvent`.
- Preserve correlation identity, timestamps, component address, event name, level, message, and sanitized invariant attributes.
- Preserve bounded, ordered, best-effort event behavior.
- Preserve the rule that component failure remains observable through component completion and does not fault the event output.
- Preserve event-output completion and disposal behavior.
- Unconsumed events must not hold component completion open.
- The selected event port name must be used in the runtime output dictionary and application address.
- An event port must participate in descriptor validation, Designer metadata, runtime validation, application linking, stable port surfaces, and authoring handles like other output ports.
- Permit zero or more explicitly named event ports if the implementation remains small and follows the same output-name uniqueness rules.
- Event ports and normal output ports share the same output-name namespace.
- Duplicate output/event names fail immediately.
- Do not add a new `ComponentPortKind.Event` unless an unavoidable existing runtime requirement proves it necessary. Prefer representing the externally linkable event port as a normal typed `ComponentEvent` output while retaining `AddEvents(...)` as the explicit source-conversion operation.
- Do not confuse component event ports with the separate reserved application-level system addresses such as `System.Events.Output`.

Audit `ComponentEvents.PortName`. It must no longer control registration, validation, or runtime attachment. Remove it if it has no honest remaining purpose. Do not retain a compatibility constant that suggests the name is still globally reserved.

## Runtime Instance Construction

The typed factory path must internally construct the final `ComponentInstance`.

After node activation:

1. evaluate input selectors in declaration order;
2. evaluate signal selectors in declaration order;
3. evaluate normal output selectors in declaration order;
4. evaluate event selectors in declaration order;
5. reject null bindings with a clear error containing the component and port context;
6. build immutable or detached runtime port collections;
7. create the `ComponentInstance`;
8. attach named event bridges;
9. retain existing completion, startup, linking, and disposal semantics.

Do not expose mutable builder collections through descriptors or runtime instances.

The runtime must continue validating that an activated instance exactly matches its descriptor. The typed path should make mismatches structurally difficult, but `ApplicationRuntimeComponentActivator.ValidateInstance(...)` remains defense in depth and must continue protecting advanced factories.

If activation, selector evaluation, event attachment, or validation fails:

- dispose the created node;
- dispose any additional owned activation resource;
- dispose any event bridges already created;
- do not leak a partially created component;
- preserve the original failure;
- preserve existing aggregate-failure behavior when cleanup also fails;
- do not perform cleanup twice.

## Processing and Source Behavior

Preserve:

- `CompositionProcessingCapabilities`;
- processing configuration before node construction where currently required;
- `IFlowSource` detection and source startup;
- component completion;
- normal completion and fault propagation;
- application revision ownership;
- stable input/output addressing;
- link cardinality enforcement;
- reliable normal-data delivery;
- best-effort diagnostic events;
- resource scoping;
- component option binding;
- keyed resources;
- application revision disposal.

The new binding builder must not start nodes or resolve services during registration.

## Options, Resources, and Display Metadata

Options and resources remain explicit root-level component declarations. Do not fold them into a universal object or add nested builders.

A designed component should remain flat:

```csharp
builder.AddComponent("sample.uppercase", component =>
{
    component.WithDisplay(
        displayName: "Uppercase",
        category: "Samples");

    component.AddOption<int>(
        "boundedCapacity",
        OptionValueKind.Number,
        displayName: "Bounded Capacity",
        defaultValue: 256,
        min: 1);

    component
        .UseFactory(static context =>
        {
            var options = context.BindConfiguration<UppercaseOptions>();
            return new UppercaseNode(options);
        })
        .AddInput(
            "Input",
            static node => node.Input,
            displayName: "Input",
            isPrimary: true)
        .AddOutput(
            "Output",
            static node => node.Output,
            displayName: "Output",
            isPrimary: true)
        .AddEvents(
            "Events",
            static node => node.Events,
            displayName: "Events");
});
```

The typed builder returned from a designed `UseFactory(...)` must support the current flat port presentation arguments:

- display name;
- group;
- order;
- summary;
- primary-port flag;
- link cardinality.

Do not require a second `DescribePort(...)` call that repeats the port name.

Preserve:

- `WithDisplay(...)`;
- option choices and attributes;
- resource metadata and attributes;
- port attributes;
- component attributes;
- metadata finalization;
- immutable catalog snapshots;
- canonical processing hints;
- immediate validation;
- idempotent equivalent registration;
- clear conflicting-registration errors.

Keep `FluxFlow.Composition` independent from the Designer package. Add the smallest explicit extension seam needed for the Designer builder to return its designed typed binding builder. Do not introduce circular references or move Designer concepts into the Composition core.

## Factory and Lifecycle Escape Hatch

Some existing Storage and Sessions component factories attach additional cleanup for component-owned store instances. Preserve that behavior.

Provide one explicit advanced path for factories that genuinely need to construct a complete `ComponentInstance` or carry additional completion/disposal ownership. Prefer a name that distinguishes it from normal node factories, such as:

```csharp
component.UseInstanceFactory(CreateSpecialComponentInstance);
```

Requirements:

- Normal packages and samples must use the typed node-factory path.
- Keep the advanced path only for concrete lifecycle needs.
- Do not make the advanced path the normal documentation example.
- Do not add obsolete overloads solely for compatibility.
- Do not weaken cleanup ownership.
- Do not move store disposal responsibility into unrelated node classes merely to avoid designing an honest activation boundary.
- If Storage and Sessions can use a single small typed activation result containing a node plus optional extra cleanup, prefer that over keeping their duplicated port declarations.
- Do not create a hierarchy of activation-result abstractions.
- A single small immutable generic activation value is acceptable when justified by the existing Storage and Sessions ownership cases.
- Preserve failure cleanup when store acquisition succeeds but later node construction or binding fails.
- Preserve node disposal plus additional cleanup aggregation.
- Keep `ComponentInstance.Create(...)` and `ComponentPorts` available only where they remain legitimate low-level runtime tools; remove them from normal component-authoring guidance.

## Registration Identity and Idempotency

Equivalent repeated family registration currently succeeds, while conflicting registration fails. Preserve that behavior.

Typed factory wrapping must not accidentally make every repeated registration look different merely because a new wrapper delegate or binding collection was allocated.

The descriptor or registration snapshot must retain an explicit comparable registration identity containing the meaningful original declarations:

- original node or instance factory delegate;
- processing capabilities;
- input binding declarations;
- signal binding declarations;
- normal output binding declarations;
- event binding declarations;
- options;
- resources.

Use explicit delegate/value comparisons. Do not use reflection, source-code text, expression-tree serialization, or last-write-wins behavior.

Tests must prove:

- repeating an equivalent built-in family registration remains idempotent;
- changing a factory conflicts;
- changing a selector conflicts;
- changing a port name, type, kind, cardinality, or event declaration conflicts;
- changing options, resources, processing, or design metadata conflicts as before.

## Validation and Error Behavior

Preserve or add immediate, clear failures for:

- null builders;
- null factory delegates;
- blank component types;
- blank port names;
- null selectors;
- duplicate input names;
- duplicate normal output/event names;
- duplicate signal names;
- duplicate options;
- duplicate resources;
- invalid link cardinality;
- missing factory;
- multiple incompatible factory modes;
- factory returning null;
- selector returning null;
- descriptor/runtime port mismatch;
- runtime message-type mismatch;
- runtime signal-kind mismatch;
- event source mismatch;
- conflicting repeated registration.

Error messages should identify the component type and relevant port where practical. Do not expose payload data, configuration values, secrets, or resource contents.

Do not catch and replace exceptions when doing so would discard their cause. Wrap only at established activation boundaries and preserve inner exceptions.

## Core Files to Inspect and Refactor

At minimum, inspect and update the cohesive contracts around:

- `src/FluxFlow.Composition/Registration/RuntimeComponentRegistrationBuilder.cs`
- `src/FluxFlow.Composition/Registration/FluxFlowRegistrationExtensions.cs`
- `src/FluxFlow.Composition/Factories/ComponentDescriptor.cs`
- `src/FluxFlow.Composition/Factories/ComponentFactory.cs`
- `src/FluxFlow.Composition/Factories/ComponentPortMetadata.cs`
- `src/FluxFlow.Composition/Runtime/ComponentPorts.cs`
- `src/FluxFlow.Composition/Runtime/ComponentInstance.cs`
- `src/FluxFlow.Composition/Runtime/ComponentEvents.cs`
- `src/FluxFlow.Composition/Runtime/ComponentEventBridge.cs`
- `src/FluxFlow.Components.Designer/ComponentRegistrationBuilder.cs`
- `src/FluxFlow.Components.Designer/ComponentRegistrationExtensions.cs`
- `src/FluxFlow.Engine/Hosting/ApplicationRuntimeComponentActivator.cs`
- `src/FluxFlow.Engine/Hosting/ApplicationRuntimePortBinder.cs`
- relevant application port-surface and link-compilation code.

Do not modify Engine code merely because it was inspected. Change it only where the explicit named event-port or generated binding contract requires it.

## Built-In Component Migration

Migrate all normal component composition packages to the authoritative typed binding API.

Audit at least these families:

- Assertions
- Expectations
- FileSystem
- HTTP
- Mapping
- Metrics
- MQTT
- Observability
- Payloads
- Projections
- Resilience
- Routing
- Serialization
- Sessions
- Sources
- State
- Storage
- Timers
- Validation

The release matrix currently covers 19 families and 44 component declarations. Preserve that complete registration inventory.

For every migrated component:

- declare every input once;
- declare every signal input once;
- declare every normal output once;
- explicitly declare its event port with `AddEvents(...)`;
- use the existing external name `"Events"` where necessary to preserve the current built-in address;
- add or reuse a family-owned `Ports.Events` constant where that family already centralizes port names;
- do not reintroduce a global reserved-name convention;
- preserve option and resource metadata;
- preserve Designer labels, groups, order, summaries, primary flags, and attributes;
- preserve node configuration and resource resolution;
- preserve processing capabilities;
- preserve external type names and existing canonical port names;
- preserve component output types and link cardinalities;
- preserve runtime behavior and disposal.

Review MQTT separately and carefully because it owns signals, transport resources, controller/session lifecycle, and multiple component shapes. Do not simplify away its ownership semantics.

Review Storage and Sessions carefully because of additional store cleanup.

## Sample and Fixture Migration

Update normal authoring examples in:

- `samples/FluxFlow.CompositionSample`
- `samples/FluxFlow.DurabilityOperationsSample`
- `samples/FluxFlow.SampleApp`
- `samples/FluxFlow.MqttCompositionSample`
- `eng/package-consumer-acceptance`
- any other sample or acceptance consumer using `AddRuntimeComponent(...)`.

The canonical sample should visibly demonstrate:

```csharp
component
    .UseFactory(...)
    .AddInput(...)
    .AddOutput(...)
    .AddEvents(...);
```

Do not leave the old duplicated authoring shape in any public sample or primary documentation.

Do not rewrite low-level `FluxFlow.Fluent` graph construction merely because it legitimately uses runtime instances and ports outside component registration. Keep that lower-level API unless the new implementation requires a focused correction.

## Testing Requirements

Follow the repository-required test-generation and test-quality workflow before changing tests. Preserve xUnit and Shouldly conventions. Do not add a new test project unless no existing project can own the behavior.

### Composition tests

Add or update focused tests proving:

- sync node factory inference;
- async node factory inference;
- the factory is not called during registration;
- selectors are not called during registration;
- descriptor metadata exists before activation;
- each selector runs exactly once per activated instance;
- input message type is inferred correctly;
- output message type is inferred correctly;
- signal binding retains signal semantics;
- cardinality is preserved;
- declaration order is preserved;
- the activated runtime ports bind the exact selected node blocks;
- no duplicated manual `ComponentPorts` declaration is needed;
- component registration callbacks still run exactly once;
- catalogs remain immutable and detached from retained builders;
- equivalent registration remains idempotent;
- conflicting registration remains explicit.

### Event tests

Update `ComponentEventTests` and related tests to prove:

- no event output is added implicitly;
- a component without `AddEvents(...)` has no event port;
- `AddEvents("Events", ...)` creates an output named `"Events"`;
- `AddEvents("Diagnostics", ...)` creates an output named `"Diagnostics"`;
- the external payload type is `ComponentEvent`;
- the selected source is `FlowEvent`;
- event conversion preserves address, identity, timestamp, name, level, message, and attributes;
- custom event ports can fan out;
- multiple event ports work if supported;
- duplicate normal-output/event names fail;
- a normal output may use `"Events"` when no event port uses that name;
- component faults remain on component completion;
- event output completion remains successful under the established contract;
- unconsumed events do not block completion;
- all bridges and sources are disposed exactly once.

Do not keep tests asserting automatic `Events` injection or global reservation.

### Designer tests

Prove:

- designed typed port calls create runtime metadata and design metadata from one call;
- designed event ports use the chosen name;
- event ports have output direction and `ComponentEvent` value type;
- display metadata and attributes remain intact;
- no implicit event metadata is added;
- metadata finalization remains immutable;
- equivalent registration remains idempotent;
- conflicting design registration still fails before mutating DI.

### Engine tests

Prove:

- generated typed instances pass exact descriptor/runtime validation;
- advanced raw instances still fail when they omit, add, rename, or mistype ports;
- validation failure cleans up the node and additional ownership;
- application links bind selected normal and event outputs correctly;
- a custom-named event output has the expected workflow address;
- application revision lifecycle remains unchanged;
- sources still start normally;
- failure cleanup remains exact.

### Component-family tests

Update affected composition test projects without replacing useful behavioral assertions with source-shape checks.

Preserve tests for:

- factory configuration;
- resource resolution;
- option binding;
- expected ports;
- Designer metadata;
- processing capabilities;
- controller/store ownership;
- disposal;
- family idempotency.

Update the release family matrix so all 19 families and 44 declarations remain covered with their explicit event ports.

### Sample and acceptance tests

Update release tests that inspect sample source or package-consumer source. Preserve the existing package-only restart durability behavior and exact markers.

Run the real package-consumer acceptance gate after migration because its fixture directly uses runtime component registration.

## Public API and Compatibility Baseline

This is an intentional breaking authoring-API simplification.

Audit:

- `eng/public-api/baseline.txt`
- `tests/FluxFlow.Release.Tests/PublicApiBaselineTests.cs`
- `tests/FluxFlow.Release.Tests/PackageBinaryCompatibilityPolicyTests.cs`
- `docs/15-engine-compatibility.md`
- the binary-compatibility release gate.

Update the accepted public API baseline through the repository’s documented intentional-change process. Do not disable, skip, weaken, or bypass compatibility verification.

Do not add obsolete compatibility wrappers solely to preserve the redundant API.

Document clearly:

- which old authoring calls were removed or moved to the advanced path;
- the new typed fluent shape;
- why the change is intentional;
- that runtime component behavior and canonical application JSON remain preserved.

Do not bump package versions or publish packages unless repository validation makes a version change mandatory and that requirement is explicitly justified in the final report.

## Documentation Requirements

Update all affected public documentation, including at minimum:

- root `README.md`;
- `docs/03-node-authoring.md`;
- `docs/04-package-authoring.md`;
- `docs/02-definitions-and-links.md` where component-event addressing is described;
- `src/FluxFlow.Composition/README.md`;
- `src/FluxFlow.Components.Designer/README.md`;
- affected component-package READMEs;
- affected sample READMEs;
- compatibility documentation.

Documentation must show:

- the new typed factory chain;
- single-authoritative input/output declaration;
- signal input declaration;
- explicit named `AddEvents(...)`;
- event-source versus public `ComponentEvent` conversion;
- absence of implicit event ports;
- custom event port names;
- the advanced instance-factory escape hatch;
- lifecycle ownership;
- no reflection or scanning;
- unchanged JSON/application-definition behavior;
- unchanged normal-data and event-delivery semantics.

Remove outdated examples containing both:

```csharp
ComponentPorts.Input/Output(...)
```

and:

```csharp
component.AddInput/AddOutput(...)
```

for the same registered component.

## Memory Requirements

Update the repository memory as part of the implementation:

1. Add a new numbered memory entry using the next available number.
2. Record:
   - the duplication that motivated the change;
   - the selected typed factory-binding design;
   - why descriptor metadata and runtime bindings remain internally distinct;
   - how one public declaration now generates both;
   - explicit named `AddEvents(...)`;
   - removal of implicit globally reserved component event ports;
   - advanced lifecycle escape-hatch decision;
   - migration scope;
   - public API break;
   - verification evidence;
   - remaining limitations.
3. Update `memory/00-index.md`.
4. Update `memory/01-current-state.md`.
5. Update the progress log only where repository convention requires it.
6. Do not rewrite historical memory entries merely to make them look current.
7. Record exact final test/build evidence, not intended commands.

## Non-Goals

Do not include:

- durability readiness adapters;
- new storage providers;
- workflow checkpointing;
- durable internal graph state;
- transactional outbox work;
- changes to SQL-file or T-SQL schemas;
- changes to delivery guarantees;
- component discovery;
- assembly scanning;
- reflection;
- source generators;
- attributes for port inference;
- nested callback builders;
- a universal component-options object;
- dependency additions without a demonstrated requirement;
- redesign of the canonical JSON document;
- redesign of the C# workflow-definition DSL;
- unrelated formatting cleanup;
- arbitrary file splitting;
- unrelated performance refactoring;
- provider or package version changes without necessity;
- new background workers;
- new test infrastructure;
- test retries or arbitrary sleeps.

## Dependency and Complexity Guardrails

- Add no dependency unless unavoidable.
- Prefer direct generic delegates, small immutable records, and explicit lists.
- Keep the dependency graph unchanged if possible.
- Do not create a large hierarchy of builder interfaces.
- Do not create generic abstractions beyond the concrete typed node-binding need.
- Keep Composition independent from Engine and Designer.
- Keep Designer dependent on Composition, not the reverse.
- Keep Engine consuming immutable descriptors and runtime instances.
- Preserve SRP:
  - registration builder owns declarations;
  - typed binding builder owns node selectors;
  - descriptor owns immutable static contract;
  - component instance owns activated bindings and lifecycle;
  - event bridge owns `FlowEvent` to `ComponentEvent` conversion;
  - Engine owns activation validation and linking.
- Prefer one or two small new types over a broad framework.
- No hidden ambient state.
- No static mutable registration state.
- No service locator.
- No factory execution at registration time.

## Verification Sequence

Run focused verification first, then broaden.

At minimum:

1. Restore the solution if required.
2. Build:
   - `FluxFlow.Composition`;
   - `FluxFlow.Components.Designer`;
   - `FluxFlow.Engine`;
   - affected component composition projects;
   - affected samples;
   - Release tests.
3. Run:
   - complete Composition tests;
   - complete Designer tests;
   - relevant Engine activation/linking tests;
   - every affected component composition test project;
   - component-family registration matrix tests;
   - public API baseline tests;
   - binary compatibility policy tests;
   - sample/documentation tests;
   - package-consumer acceptance script tests.
4. Run the real package-consumer acceptance gate.
5. Run the complete Release test project.
6. Run the complete Release solution build with zero warnings.
7. Run the complete Release solution test suite.
8. Run formatting verification for every touched project.
9. Run `git diff --check`.
10. Confirm no unexpected package dependency changes.
11. If dependencies changed for a justified reason, run the repository vulnerability audit.
12. Confirm no database schema, migration, generated artifact, credential, secret, or local build output was accidentally added.
13. Confirm the working tree contains the pre-existing durability restart changes plus this coherent component-authoring work, with no unrelated user changes lost.

Do not run unrelated server-backed integration suites unless a changed dependency boundary or failing test shows they are relevant.

## Final Acceptance Criteria

The goal is complete only when all of the following are true:

- The canonical runtime-only sample uses the new typed fluent chain.
- Each input name appears once.
- Each output name appears once.
- Each signal-input name appears once.
- Each event-port name appears once.
- Port message types are inferred from node selectors in normal usage.
- Normal component authors do not construct `ComponentInstance`.
- Normal component authors do not call `ComponentPorts`.
- `AddEvents(name, selector)` explicitly declares a named event output.
- No component event output is injected automatically.
- `"Events"` is not globally reserved.
- Existing built-in components explicitly retain their established `"Events"` address where required.
- Event conversion and completion behavior remain correct.
- Runtime descriptor/instance validation remains active.
- Advanced completion and additional-disposal ownership remain supported.
- Storage and Sessions cleanup remains exact.
- MQTT ownership and signal behavior remain exact.
- Options, resources, processing, Designer metadata, and application linking remain intact.
- All 19 component families and 44 declarations remain registered.
- Equivalent repeated registration remains idempotent.
- Conflicting registration remains explicit.
- Canonical application JSON behavior remains unchanged.
- No reflection, scanning, hidden convention, nested callback, or new dependency graph was introduced.
- Public API and binary compatibility baselines honestly record the intentional break.
- Samples, documentation, goal records, and memory are updated.
- Focused and full verification are green with zero warnings.
- No unrelated functionality was removed.
- No unrelated cleanup was mixed into the change.

## Final Report

At completion, report:

- the final public authoring API;
- the internal ownership design;
- how port metadata and runtime binding are generated from one declaration;
- event-port behavior and naming;
- the advanced lifecycle escape hatch;
- migrated component families and samples;
- intentional public API changes;
- files and documentation updated;
- focused test results;
- full build and test results;
- package-consumer acceptance result;
- formatting and compatibility results;
- dependency status;
- any remaining limitations or consciously deferred work.

Do not claim completion until implementation, migration, documentation, memory, compatibility baselines, and required verification are all finished.

## Execution Evidence

Completed 2026-08-08.

- Added the typed runtime and designed binding builders, immutable typed node
  activation result, and named low-level event-source contract.
- Removed duplicated normal authoring, automatic event-port insertion, the
  global `Events` reservation, and the obsolete `ComponentEvents` constant.
- Migrated every one of the 19 component families and 44 declarations plus all
  named samples and the package-consumer fixture. Storage/Sessions additional
  cleanup and MQTT resource/signal ownership remain explicit and tested.
- Updated root/package documentation, compatibility guidance, the documented
  public source-declaration baseline, goals, and memory. Published binary
  baselines remain active so the intentional break cannot pass future package
  validation without an appropriate major release.
- Focused results: Composition 139/139, Designer 120/120, Engine assembler
  18/18, Storage composition 21/21, and Release convention/matrix/docs 59/59;
  all completed with zero warnings.
- Complete `FluxFlow.Release.Tests`: 169/169, zero warnings.
- Complete CI-style Release build: 134 projects, zero errors and zero warnings.
- Complete Release solution test: 2,561/2,561 across 66 projects, zero warnings
  and zero skips.
- The real package-consumer `-PackPackages` gate passed exact nine-package
  archive verification, external `net8.0` build, canonical behavior, SQL-file
  reopen, and all process-restart recovery/idempotency markers.
- Full formatting verification, `git diff --check`, public API/binary-policy
  tests, and the transitive vulnerable-package audit passed.
- No typed-authoring dependency, package version, database schema, migration,
  publication, tag, release, commit, push, or pull request was introduced.
