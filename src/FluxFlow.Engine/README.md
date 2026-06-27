# FluxFlow.Engine

Optional advanced executable runtime for hosts that intentionally use the older
`ApplicationDefinition` model.

For new component packages and host composition, start with `FluxFlow.Nodes`,
`FluxFlow.Composition`, and `FluxFlow.Composition.Hosting`. Component packages
do not need this package to expose standalone nodes, composition adapters, or
Designer metadata.

## When To Use It

Use `FluxFlow.Engine` when a host already depends on:

- `ApplicationDefinition` workflow documents
- engine-specific validation and runtime build errors
- conditional links through engine definitions
- `ApplicationRuntimeBuilder` or `FlowApplicationHost`
- engine lifecycle state and diagnostic streams

If a host only needs to compose standalone nodes from fluent C# or
`IConfiguration`, use `FluxFlow.Composition` instead.

## Public Surface

The package exposes the engine-era namespaces:

- `FluxFlow.Engine`
- `FluxFlow.Engine.Components`
- `FluxFlow.Engine.Definitions`
- `FluxFlow.Engine.Runtime`

`FluxFlow.Mapping` owns expression and mapping contracts. The engine consumes
those contracts for link conditions but does not own concrete expression
languages.

## Component Boundary

Normal component packages should remain engine-free. Reusable node behavior
belongs in packages built on `FluxFlow.Nodes`; composition-facing registration
and design metadata belong in optional `.Composition` packages.

See `docs/15-engine-compatibility.md` for the compatibility policy and
`docs/12-component-composition.md` for the standalone-first component model.
