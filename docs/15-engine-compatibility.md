# Engine Compatibility

`FluxFlow.Engine` 6.x is the optional canonical application host. It consumes
`FluxFlow.Composition.Model.ApplicationDefinition`, activates explicit
component descriptors, owns transactional revisions, and exposes stable ports,
system events, diagnostics, and lifecycle state through `FluxFlowApplication`.

Component packages release independently and remain Engine-free.

## Stable Surface

The host-level surface is intentionally small:

- `FluxFlowApplication` and `FluxFlowApplicationOptions`.
- `ApplicationState`, `ApplicationSnapshot`, `ApplicationUpdateResult`, and
  update status/diagnostic contracts.
- `ApplicationPorts` and operation contracts in `FluxFlow.Engine.Ports`.
- definition sources in `FluxFlow.Engine`.
- operational events and diagnostics in `FluxFlow.Engine.Signals`.
- explicit legacy document migration in `FluxFlow.Engine.Migration`.

The canonical document, addressing, component descriptors, link compiler, and
resource registrar are versioned by `FluxFlow.Composition`. Standalone nodes
and messages are versioned by `FluxFlow.Nodes`.

Runtime assemblers, revision planners and candidates, provider snapshots, port
generations, routing snapshots, binders, leases, queue layout, visitors, and
fanout pumps are implementation details rather than extension points.

## Version Policy

Patch releases preserve source and binary compatibility for normal consumers.
Minor releases may add optional diagnostics, result fields, overloads, or host
integration helpers with safe defaults. Major releases may remove public
contracts or intentionally change lifecycle or persisted boundaries and require
migration guidance, API comparison, package validation, and consumer builds.

Engine 6 intentionally replaces the public host/coordinator/assembler split
with one application facade. It also internalizes runtime generation and
provider ownership APIs. These are approved major-version breaks; unrelated
public API breaks are not accepted.

`FluxFlow.Composition.Hosting` 6.x is a thin obsolete compatibility package. It
forwards practical legacy registration and host APIs to Engine but owns no
lifecycle, synchronization, snapshot, runtime, or provider state. It is planned
for removal in the next major release.

## Revision Guarantees

Lifecycle mutations are serialized. New revisions are prepared away from live
routing. Failed candidates are disposed and cannot replace the active revision.
A successful candidate activates before the stable port facade switches; the
old candidate drains and is then disposed. Revision-owned providers and
candidates are disposed exactly once.

Expected source, normalization, validation, preparation, and activation
failures return `ApplicationUpdateStatus.Rejected`. Caller cancellation remains
cancellation. Component-level `FlowError` values remain workflow data and do
not define application host lifetime.

## Canonical Definition Compatibility

Applications should keep product workspace data under host ownership and
project only executable `Resources` and `Workflows` into the canonical
Composition definition. Normal startup must not deserialize retired Engine
shapes.

`LegacyEngineApplicationDefinitionMigrator` is the explicit one-way boundary
for compatible old Workflows/Nodes JSON. Persist the canonical result after
migration; do not run migration on every startup as a second persistence mode.

## DI And Resource Compatibility

Engine uses standard `IServiceCollection` and keyed registrations.
`IApplicationResourceRegistrar` from Composition is the adapter extension
point. Registrars populate revision-owned service collections; explicitly
bridged external instances remain host-owned. Compatibility does not imply
provider fallback, automatic provider merging, reflection, or assembly
scanning.

## Expression And Component Compatibility

`FluxFlow.Mapping` owns expression, mapper, predicate, and context contracts.
Engine consumes them for link conditions but does not own a concrete expression
language.

Component packages expose reusable behavior through `FluxFlow.Nodes`, optional
registration through `.Composition`, and optional Designer metadata. They do
not reference Engine. Hosts pin package families independently and run
activation and port-contract tests after upgrades.

Next: [Engine 2 To 3 Migration](23-engine-2-to-3-migration.md)
