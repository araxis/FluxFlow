# Engine Compatibility

`FluxFlow.Engine` version 3 is the optional canonical runtime package. It
assembles `FluxFlow.Composition.Model.ApplicationDefinition` revisions into
executable components, compiled routes, stable ports, system events, and
diagnostics. It does not own a separate definition, node, or lifecycle model.

Component packages release independently and remain Engine-free.

## Stable Surface

The stable Engine 3 surface includes public contracts in:

- `FluxFlow.Engine.Hosting`
- `FluxFlow.Engine.Migration`
- `FluxFlow.Engine.Ports`
- `FluxFlow.Engine.Signals`

The canonical document and addressing contracts are versioned by
`FluxFlow.Composition`. Revision hosting and provider snapshots are versioned by
`FluxFlow.Composition.Hosting`. Standalone nodes and message envelopes are
versioned by `FluxFlow.Nodes`.

Internal queue layout, routing snapshots, visitors, generation references,
activation rollback helpers, instrumentation adapters, and fanout pumps are not
public extension points.

## Patch Releases

Patch releases preserve source and binary compatibility for normal consumers.
They may contain behavior-preserving fixes, test hardening, clearer diagnostics,
or performance changes. They must not remove public members, change stable-port
acceptance semantics, or require new host services.

## Minor Releases

Minor releases may add optional ports, status fields, diagnostics, overloads,
or host integration helpers with safe defaults. They must preserve existing
revision, routing, and direct-port behavior.

## Major Releases

Major releases may remove public contracts or intentionally change lifecycle,
routing, status, or persisted compatibility boundaries. Breaking changes must
include migration guidance, public API comparison, package validation evidence,
and a clean package consumer build.

Engine version 3 intentionally removed the former mutable Engine definition,
JSON/validator family, node authoring bases, runtime factory registry, runtime
builder, lifecycle host, state streams, and numeric error/diagnostic models.
Those declarations are not retained as compatibility shims.

## Canonical Definition Compatibility

Applications should keep product workspace data under host ownership and
project only executable `Resources` and `Workflows` into the canonical
Composition definition. Normal loading, persistence, validation, and activation
must never deserialize the retired Engine shape.

`LegacyEngineApplicationDefinitionMigrator` is the explicit one-way boundary
for compatible old Workflows/Nodes JSON. It flattens configuration and port
properties and translates old `From`/`When` link objects into canonical
`Port`/`Condition` declarations. It rejects:

- executable resource nodes, which must become host-owned services/resources
- non-default `Phase`, which must become a semantic processing profile
- resource-node links
- ambiguous flat-property collisions
- unknown or malformed document properties

Persist the canonical result after migration. Do not run migration on every
startup as a second supported persistence mode.

## Provider Snapshot Compatibility

`FluxFlow.Composition.Hosting` snapshots are immutable ownership boundaries over
normal Microsoft DI providers. Hosts compose service collections explicitly and
bridge exact external instances explicitly. Compatibility does not imply
provider fallback, assembly scanning, automatic provider merging, or ownership
of externally supplied clients and stores.

## Expression Compatibility

`FluxFlow.Mapping` owns `IFlowExpressionEngine`, compiled expression, mapper,
predicate, and context contracts. Engine consumes those contracts for canonical
link conditions but does not own a concrete expression language.

Applications that persist conditions own expression syntax, validation,
available variables, migrations, and adapter dependencies. Canonical runtime
delivery exposes `input`, `payload`, and `message` variables.

## Component Package Compatibility

Component packages expose reusable behavior through `FluxFlow.Nodes`, optional
registration through `.Composition`, and optional Designer metadata. They do
not reference Engine. Hosts pin and update package families independently and
run workflow activation tests after upgrades.

Next: [Engine 2 To 3 Migration](23-engine-2-to-3-migration.md)
