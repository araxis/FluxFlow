# Unified Code-First Component Contracts

Date: 2026-08-08

## Decision

FluxFlow now uses `ComponentContract` as the complete compiled-C# declaration of
a component. The former authoring-only contract required application authors to
add a typed component to a workflow and then repeat its factory and ports in
`AddRuntimeComponent`. That split was verbose and allowed the handle, metadata,
and executable descriptor to drift.

The complete contract owns:

- the canonical component type;
- one immutable `ComponentDescriptor`;
- the runtime node or instance factory;
- typed input, signal-input, output, and explicitly named event bindings;
- the typed authoring handle;
- optional component-specific options creation and application.

Construction remains explicit and flat. It reuses
`RuntimeComponentRegistrationBuilder`; there is no reflection, scanning,
source generation, ambient state, global registry, or delegate inference.
Factories are retained at declaration time but execute only during activation.

## Application definition ownership

`ApplicationDefinitionBuilder` shares a small descriptor collection across its
workflow builders. Adding a complete contract first validates options and the
handle, then atomically commits the component and descriptor. Reusing the exact
contract across components or workflows stores one descriptor. A different
descriptor for the same type is rejected with the type in the diagnostic.

`ApplicationDefinition.ComponentDescriptors` is immutable, read-only, and
deterministically ordered by type. Plain constructor-created and JSON-loaded
definitions have an empty collection. The JSON converter continues to read and
write only `Resources` and `Workflows`; it never serializes factories,
selectors, handles, descriptors, or delegates.

## Runtime catalog and revisions

Engine resolves one effective `ComponentCatalog` per candidate revision:

```text
host-registered descriptors + definition-owned descriptors -> effective catalog
```

The same effective catalog is passed to link compilation, port-surface
creation, validation, and activation. Exact descriptor-reference reuse is
accepted. Different descriptor instances with the same type conflict; no source
wins silently.

Descriptor reference identity is the explicit identity of code behavior.
Reusing one contract is revision-stable. Introducing, removing, or replacing a
used descriptor updates the affected workflow without reflecting over or
hashing delegate bodies. Failed candidates preserve the active generation.
Successful replacement retires the old generation and allows captured factory
state to be collected after normal retirement.

Revision resource snapshots remain primary during activation and are owned by
Engine. Ordinary host services are an explicit fallback through
`ComponentActivationContext.Services`; the fallback host provider is never
disposed by a revision.

## Registration boundaries

Normal compiled-C# hosting is:

```csharp
var definition = application.Build();
services.AddFluxFlow(definition);
```

JSON and dynamic string definitions contain no executable C# and therefore
register required complete contracts or family extensions explicitly. The
low-level `AddRuntimeComponent(type, configure)` path remains for genuinely
dynamic descriptors; it is not a second step after typed code-first authoring.

## Designer and official families

`DesignedComponentContract.Create` adapts the existing flat designed-component
builder into one exact descriptor plus presentation metadata. Official family
registration consumes that contract, so Designer metadata and runtime behavior
share one declaration and metadata inspection never activates factories.

All 19 active Composition families and 44 contracts use the complete model.
Their typed options, handles, port names, explicit Events outputs, resource
requirements, processing capabilities, and MQTT lifecycle boundaries remain
unchanged.

## Samples and package consumer

The Composition sample, SampleApp, MQTT composition sample, and isolated package
consumer declare custom components once and omit redundant runtime registration
in their code-first modes. Genuine services remain normal DI registrations.
JSON modes still register contracts explicitly, and MQTT still registers its
resource registrar because it owns client/resource lifecycle rather than merely
describing a component.

## Verification evidence

Final verification completed on 2026-08-08:

- focused Composition contract/JSON/registration coverage: 51 passed, zero
  failures, skips, and warnings;
- focused Designer exact-descriptor/metadata/no-activation coverage: 17 passed,
  zero failures, skips, and warnings;
- focused Engine catalog/revision/rollback/lifetime/DI coverage: 52 passed,
  zero failures, skips, and warnings;
- focused Release public-surface, 19-family/44-contract, source-shape, and
  package-fixture coverage: 12 passed; the four legacy metadata-governance
  facts and 14 documentation-boundary facts also pass;
- real isolated `-PackPackages` acceptance: candidate package closure, explicit
  JSON registration, embedded-contract code-first execution, durability restart
  markers, and runner cleanup all passed;
- full Release build: 134 projects, zero warnings and errors;
- full solution: 2,597 tests across 66 projects, zero failures, skips, and
  warnings;
- dedicated Release governance: 174 tests, zero warnings;
- public API baseline: intentionally accepted and independently verified 2/2;
- full `dotnet format --verify-no-changes` and `git diff --check`: exit 0;
- dependency audit: no vulnerable direct or transitive package in any solution
  project;
- hygiene scans: no removed authoring-contract use, reflection/scanning/dynamic
  invocation/global contract registry, normal-sample duplicate runtime
  registration, skipped test, TODO, or FIXME in the touched slice.

Executable evidence:

- Composition sample: `ALPHA`, `BETA` using only `AddFluxFlow(definition)` for
  its complete contracts;
- SampleApp: three correctly routed orders and six component events;
- MQTT sample: JSON/configuration and compiled code-first paths each published
  the two expected acknowledgements;
- durability operations sample: one input delivered, one output captured and
  completed, with explicit terminal status snapshots.

## Intentional limits

- Executable contracts are not serialized to JSON.
- JSON does not discover contracts automatically.
- No assembly scanning, source generation, UI-to-C# export, or global registry
  was added.
- Dynamic string components still require explicit host registration.
- Resource/backend settings, durability guarantees, and MQTT resource ownership
  were not redesigned.
- No package was published and no release was created in this round.
