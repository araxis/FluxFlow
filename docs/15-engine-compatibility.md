# Engine Compatibility

`FluxFlow.Engine` 7.x is the optional canonical application host. It consumes
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

Engine 7 removes the remaining compatibility forwarding and alias-normalization
surface. The single application facade, canonical Composition definition, and
standard keyed DI are the supported integration points.

## Revision Guarantees

Lifecycle mutations are serialized. New revisions are prepared away from live
routing. Failed candidates are disposed and cannot replace the active revision.
A successful candidate activates before the stable port facade switches; the
old candidate drains and is then disposed. Revision-owned providers and
candidates are disposed exactly once.

Expected source, validation, preparation, and activation
failures return `ApplicationUpdateStatus.Rejected`. Caller cancellation remains
cancellation. Component-level `FlowError` values remain workflow data and do
not define application host lifetime.

## Canonical Definition Compatibility

Applications should keep product workspace data under host ownership and
project only executable `Resources` and `Workflows` into the canonical
Composition definition. Normal startup must not deserialize retired Engine
shapes.

No in-process legacy converter is shipped. Convert old Workflows/Nodes JSON
outside the runtime, make host decisions for executable resource nodes and
non-default phases, and persist only the canonical result.

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

## Typed Component Authoring Break

The component authoring surface intentionally replaces the old combination of
`UseFactory(ComponentFactory)` plus separate `AddInput<T>`/`AddOutput<T>` calls.
Normal registrations now return a typed builder from `UseFactory` and declare
each input, signal, output, and event once with a node selector. This is a
source- and binary-breaking authoring change, accepted through the public API
baseline process; it does not change canonical application JSON, component
addresses, normal-data delivery, or Engine's exact descriptor/instance
validation.

The typed builder uses `HasInput`, `HasSignalInput`, `HasOutput`, and
`HasEvents`. These names are declarative: the node already owns the selected
Dataflow member, while the component declaration maps an external port name to
it. The short-lived typed `Add...` names were removed rather than retained as
aliases; this is another intentional authoring-only break with no runtime or
JSON behavior change.

Event outputs are explicit through `HasEvents(name, selector)`. Engine no
longer injects or reserves `Events`; built-in packages explicitly retain that
name to preserve their established addresses. The low-level
`UseInstanceFactory` path remains for complete-instance ownership, and
`ComponentNodeActivation<TNode>` carries optional completion or extra cleanup
without weakening revision disposal.

The manifest's published binary baselines remain unchanged during this
unpublished source round. Consequently, the compatibility-aware packaging gate
continues to report the intentional break against those releases. Any future
publication of the affected packages must use an appropriate major version and
pass that gate; this round neither weakens the comparison nor republishes an
existing version.

Next: [Major Surface Reset](23-engine-2-to-3-migration.md)
