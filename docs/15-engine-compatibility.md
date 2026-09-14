# Engine Compatibility

`FluxFlow.Engine` 7.x is the optional canonical application host. It consumes
`FluxFlow.Json.Model.ApplicationDefinition`, activates explicit
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
registration through `.Composition`, and independent UI integration. They do
not reference Engine. Hosts pin package families independently and run
activation and port-contract tests after upgrades.

## Typed Component Definition Break

Registration accepts a type and a synchronous or asynchronous node factory.
Each factory declares native blocks with context.HasInput/HasOutput/HasEvents.
Context.SetLifetime attaches additional completion and cleanup ownership. Components
return IFlowNode; the former contract, selector and activation-wrapper APIs are
removed. The constructor API prevents noncanonical block types from being declared.
Failed port validation still cleans returned nodes and attached ownership.

Typed AddX methods embed the same factory callbacks used by JSON/DI. Links compile
against actual instances. Resource registrars remain a separate supported feature.

The manifest's published binary baselines remain unchanged during this
unpublished source round. Consequently, the compatibility-aware packaging gate
continues to report the intentional break against those releases. Any future
publication of the affected packages must use an appropriate major version and
pass that gate; this round neither weakens the comparison nor republishes an
existing version.

Next: [Major Surface Reset](23-engine-2-to-3-migration.md)
