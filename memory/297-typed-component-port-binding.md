# Typed Component Port Declaration And Binding

Date: 2026-08-08

## Result

Component registration now has one authoritative, flat, strongly typed port
declaration. Normal authors return an `IFlowNode` from `UseFactory(...)` and
chain `AddInput`, `AddSignalInput`, `AddOutput`, and explicit named
`AddEvents` selectors. The port name and inferred message type are no longer
repeated inside a manually created `ComponentInstance`.

This is an intentional source- and binary-breaking authoring simplification.
Canonical application JSON, component type names, built-in port addresses,
processing behavior, resource resolution, and delivery semantics remain
unchanged. No package was published and no compatibility shim was retained.

## Ownership Design

- `RuntimeComponentRegistrationBuilder` owns factory mode, immutable metadata,
  and the ordered binding declarations.
- `RuntimeComponentBindingBuilder<TNode>` exposes typed selectors and returns
  itself, keeping normal registration to one flat fluent level.
- A selector call creates static descriptor metadata immediately and stores a
  typed delegate for later activation. Factories and selectors do not run at
  registration time.
- After a node is activated, the runtime evaluates input, signal, normal
  output, and event selectors in deterministic declaration order and creates
  the low-level `ComponentInstance` internally.
- Descriptor/instance validation remains Engine-owned defense in depth. The
  typed path makes mismatch difficult; the explicit `UseInstanceFactory`
  escape hatch continues to be checked exactly.
- Original factory and selector delegates remain part of registration
  identity. Equivalent repeated family registration stays idempotent, while
  changed factories, selectors, metadata, or factory modes conflict.

Descriptor metadata and activated Dataflow bindings are still separate
internal concepts because linking and Designer tooling need a static contract
before any node exists. The simplification makes one public typed declaration
generate both rather than pretending the two runtime responsibilities are the
same object.

## Explicit Event Ports

Component events are no longer injected and the name `Events` is no longer
globally reserved. `AddEvents(name, selector)` records a normal public output
of `ComponentEvent`, selects an `ISourceBlock<FlowEvent>` after activation, and
creates the existing bounded ordered best-effort conversion bridge under the
chosen runtime output name.

Components may omit event ports or choose names such as `Diagnostics`. Normal
outputs and event outputs share one output-name namespace. Built-in families
explicitly use their family-owned `Ports.Events` constant, preserving their
established `Workflow.Component.Events` addresses without a global naming
convention. Application-level `System.Events.Output` remains a separate
reserved system address.

## Lifecycle Escape Hatch

`UseInstanceFactory(...)` is the explicit low-level route for a factory that
must construct an entire `ComponentInstance`. Normal packages and samples do
not use it. The small immutable `ComponentNodeActivation<TNode>` value covers
the narrower Storage and Sessions requirement: a typed node plus optional
completion and additional asynchronous cleanup.

Selector, event-attachment, and later Engine validation failures dispose the
node and additional owned resource exactly once, aggregating cleanup failure
without hiding the activation failure. Storage and Sessions preserve their
factory-owned store leases; shared stores remain host-owned. MQTT continues to
resolve and start revision-owned controllers and preserves its Ack/Nak signal
targets and node lifecycle.

## Migration And Public Surface

- All 19 maintained component composition families and all 44 declarations use
  typed node factories, selector-based ports, and explicit event ports.
- Composition, durability operations, sample application, MQTT composition,
  and package-consumer acceptance sources use the canonical shape.
- Low-level `FluxFlow.Fluent` graph construction intentionally retains
  `ComponentInstance` because it is not component registration.
- Designer mirrors the typed chain while keeping display name, group, nullable
  explicit order, summary, primary flag, cardinality, attributes, immutable
  catalog finalization, and immediate conflict validation. An omitted order
  preserves deterministic direction-local declaration order.
- `ComponentEvents` was removed. Low-level named event construction is
  represented by `ComponentEventSource` and `ComponentPorts.Events(...)`.
- The documented source-declaration baseline was accepted for the intentional
  API change. Published binary comparison baselines remain unchanged so a
  future package operation still detects the break and requires an appropriate
  major release; no existing version was republished.

## Verification Evidence

- `FluxFlow.Composition.Tests`: 139 passed, zero warnings.
- `FluxFlow.Components.Designer.Tests`: 120 passed, zero warnings.
- `ApplicationRuntimeAssemblerTests`: 18 passed, zero warnings.
- Storage composition: 21 passed, including transferred lease cleanup and
  construction-failure cleanup, zero warnings.
- Focused Release conventions, family matrix, metadata, and documentation: 59
  passed, zero warnings.
- Complete `FluxFlow.Release.Tests`: 169 passed, zero warnings. This includes
  the public API baseline, binary compatibility policy, all 19 families/44
  declarations, samples, documentation, and package-consumer script contracts.
- `PublicApiBaselineTests`: 2 passed through the documented acceptance process;
  the reviewed source-declaration baseline changed for the 21 intended package
  entries and then passed normally without the acceptance switch.
- Complete CI-style Release build: 134 projects, zero errors, zero warnings.
- Complete Release solution suite: 2,561 passed across 66 projects, zero
  warnings and zero skips.
- The real package-consumer `-PackPackages` gate packed and byte-verified the
  exact nine-package closure, restored and built the isolated external `net8.0`
  consumer with zero warnings, and passed Engine, Fluent, SQL-file reopen,
  seed/recovery, workflow capture, pending-output resumption, output recovery,
  idempotency, restart, and final completion markers.
- Full-solution `dotnet format --verify-no-changes --no-restore` and
  `git diff --check` passed.
- The full transitive vulnerable-package audit reported no vulnerable package
  for every solution project.
- No typed-authoring project dependency, package version, provider schema,
  migration, generated artifact, credential, or secret was added. The only
  project-file dependency diff is the pre-existing restart fixture's direct
  Generic Host reference recorded in memory 296.

## Remaining Boundaries

- This change does not add reflection, discovery, source generation, dynamic
  binding, nested callbacks, dependencies, or persistence behavior.
- It does not redesign workflow JSON, the C# workflow DSL, durability,
  checkpointing, providers, schemas, or delivery guarantees.
- Event delivery remains bounded best-effort diagnostics. Normal data delivery
  and component completion remain the reliable/fault-observable channels.
- Binary compatibility is intentionally not claimed against the currently
  published authoring packages; publication and version advancement remain a
  separate release operation.
