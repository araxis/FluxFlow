# DI-First Application And Component Simplification

Date: 2026-07-27

## Outcome

FluxFlow now uses standard .NET dependency injection as the only maintained
public registration surface for application composition and component
activation. Mutable composition registries, registry contributors, transitional
builder APIs, and delegate registration wrappers were removed instead of being
kept as a second framework beside `IServiceCollection`.

The work remains local on `work/di-first-application-components`. No branch was
pushed, and no tag, package publication, pull request, or merge was created.

## Canonical Registration Model

- `ComponentDescriptor` is the immutable registration record for one canonical
  component type, aliases, factory, typed ports, link cardinality, and processing
  capabilities.
- Descriptors and resource type aliases are ordinary singleton enumerable DI
  services. `AddFluxFlowComponent(...)` and the family-specific
  `Add...Components()` methods are the public registration path.
- `ComponentCatalog` is an immutable concrete snapshot built from registered
  descriptors and aliases. It uses ordinal, deterministically ordered lookup,
  validates conflicting registrations, supports semantic idempotence, resolves
  aliases to canonical types, and activates components through the captured
  factory and service provider.
- No `IComponentCatalog` abstraction was added. Consumers depend on the concrete
  immutable catalog because it already is the stable application snapshot.
- Runtime resources, ports, and other addressable services continue to use keyed
  DI registrations. Removing the component registry did not weaken keyed
  runtime resolution or ownership boundaries.
- Reflection discovery and assembly scanning remain unsupported. Registration is
  explicit, deterministic, and visible in the application's service collection.

## Application Hosting

The follow-up hosted Engine consolidation in
`memory/266-hosted-engine-simplification.md` supersedes the package split
described during this pass:

- `AddFluxFlow(...)` in Engine now composes definition sources, component
  catalog, runtime assembly, revision resources, stable ports, and hosted
  lifecycle through normal service registration.
- `FluxFlowApplication` is the single lifecycle owner for direct and hosted use.
  Revision preparation still builds an isolated provider and catalog and
  preserves transactional prepare, activate, swap, rollback, drain, and
  external-resource ownership behavior.
- `IApplicationResourceRegistrar`, its context, and keyed DI helpers now live in
  Composition because they are reusable activation contracts rather than host
  orchestration.
- Configuration definition sources, revision planning, and runtime coordination
  now live in Engine. Composition.Hosting is obsolete compatibility forwarding
  only and has no independent coordinator.

## Removed Compatibility Surfaces

The following maintained public concepts were removed or replaced:

- `CompositionNodeRegistry`, `CompositionNodeRegistration`,
  `CompositionNodeFactory`, and `CompositionNodeFactoryContext`.
- `ICompositionNodeRegistryContributor` and family registry-extension methods.
- `ApplicationHostingBuilder` and `ApplicationRuntimeAssemblerBuilder`.
- `IApplicationRuntimeServicesContributor` and
  `ApplicationRuntimeServicesContext`.
- Delegate-based `AddApplicationResources(...)` wrappers.
- Transitional `CompositionComponentTypeDescriptor`.
- Legacy Composition runtime terminology, including `CompositionRuntime`,
  `ComposedNode`, composition port types, and composition component event names.

Their canonical replacements use `ComponentDescriptor`, `ComponentFactory`,
`ComponentActivationContext`, `ComponentCatalog`, `ApplicationRuntime`,
`ComponentInstance`, component port types, and `ComponentEvent` terminology.

## Component Families

Nineteen active composition adapters now expose family-level DI registration and
canonical `*ComponentTypes`, `*ComponentPortNames`, and
`*ComponentResourceNames` constants:

- Resilience, MQTT, Mapping, Assertions, Sources, Routing, Validation,
  FileSystem, Observability, Timers, Payloads, HTTP, Serialization, Metrics,
  Projections, Expectations, Sessions, State, and Storage.
- Control already used the canonical DI shape and remains on its existing
  package line; it was not version-bumped merely for consistency.
- Each migrated family registers immutable component descriptors and exactly one
  Designer metadata provider. Family README examples use `IServiceCollection`
  and the corresponding `Add...Components()` method.
- MQTT resource activation now uses `IApplicationResourceRegistrar`; its
  resource indexing, validation, registration, client lifecycle, and ownership
  contracts remain unchanged.

## Designer Authority

- `ComponentCatalog` is authoritative for canonical type names, aliases, typed
  ports, input cardinality, and processing capabilities.
- Explicit Designer providers remain authoritative only for UI descriptions,
  option hints, resource hints, labels, sections, ordering, and other
  presentation metadata.
- `ComponentDesignMetadataCatalog` requires a component catalog when combining
  providers. The provider-only overload was removed so UI metadata cannot create
  a second component type system.
- Designer persistence and the sample host now resolve metadata against the same
  immutable catalog used for runtime activation.

## Package Versions

The 25 packages with changed public contracts use major versions:

- Composition `5.0.0`, Composition.Hosting `5.0.0`, Engine `5.0.0`, and
  Designer `4.0.0`.
- Fluent `3.0.0` and Fluent.Hosting `3.0.0`.
- Resilience Composition `3.0.0`.
- MQTT, Mapping, Assertions, Sources, Routing, Validation, FileSystem,
  Observability, Timers, HTTP, Expectations, Sessions, State, and Storage
  Composition `5.0.0`.
- Payloads, Serialization, Metrics, and Projections Composition `4.0.0`.

Package release notes, package READMEs, the top-level changelog, public API
overview, component coverage matrix, migration guide, cleanup ledger, and public
source-declaration baseline describe these boundaries. No package was published.

## Verification

- Full solution test sweep: 1,726 passed, 0 failed, 0 skipped across 65 test
  projects.
- Release tests: 99 passed with zero warnings.
- Controlled Debug and Release builds: 137 projects each, zero warnings and zero
  errors.
- The public API baseline was regenerated after the intentional removals and the
  baseline test passed.
- A fresh temporary source outside the repository was seeded by packing all 62
  manifest packages from the verified Release build; all 62 package attempts
  succeeded.
- All 25 changed packages passed release preflight.
- All 25 changed packages passed package dry-run with archive inspection,
  temporary consumer restore/build, and feed verification against the complete
  local source plus NuGet.
- SDK binary compatibility checks used each package's exact preceding published
  version. Fluent.Hosting remained binary-compatible. The other 24 packages
  reported only `CP0001`, `CP0002`, or `CP0008` diagnostics for the intentional
  public removals, renames, signature changes, and moved contracts. No NuGet,
  baseline restore, or package-source failure occurred, and no suppressions were
  added.

## Follow-Up Boundary

Any future application-registration work must build on `IServiceCollection`,
`ComponentDescriptor`, `ComponentCatalog`, keyed DI,
`IApplicationResourceRegistrar`, and the Engine-owned `FluxFlowApplication`.
Reintroducing a mutable registry, general contributor framework, second public
application coordinator, provider-only Designer catalog, or reflection discovery
would create another source of truth and requires a separately justified design.
