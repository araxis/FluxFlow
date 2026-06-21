# Standalone Composition Layer

Date: 2026-06-21

## Decision

`FluxFlow.Composition` is the official default composition layer for
standalone-node workflows.

The package references `FluxFlow.Nodes` and does not reference
`FluxFlow.Engine`. Component packages remain pure node packages first; they can
optionally add composition registration extensions later.

## Implemented

- Added `src/FluxFlow.Composition`.
- Added composition-owned DTOs:
  - `CompositionDefinition`
  - `WorkflowDefinition`
  - `NodeDefinition`
  - `LinkDefinition`
  - `NodeReference`
  - `PortReference`
- Added `CompositionDefinitionJson` with string/object reference converters.
- Added explicit node factory registration through `CompositionNodeRegistry`
  and `CompositionNodeRegistration`.
- Added factory context helpers for binding node configuration and reading
  named resource references.
- Added typed composition port wrappers and metadata:
  - `CompositionInputPort<T>`
  - `CompositionOutputPort<T>`
  - `CompositionPorts`
  - `CompositionPortMetadata`
- Added `ComposedNode` descriptors that carry node instances, named input and
  output ports, optional event/error sources, completion, and disposal hooks.
- Added fluent builders that produce the same DTO model as configuration.
- Added `CompositionConfigurationLoader` for `IConfiguration`, defaulting to
  `FluxFlow:Composition`.
- Added `CompositionValidator` for unknown node types, missing nodes/ports,
  duplicate links, and metadata-backed type mismatches.
- Added `CompositionRuntimeBuilder` and `CompositionRuntime` for direct
  Dataflow linking, start/stop/dispose, completion, and event/error aggregation.
- Added reload-facing contracts without live reload implementation:
  `ICompositionDefinitionSource`, `ICompositionReloadPlanner`,
  `CompositionReloadRequest`, and `CompositionReloadPlan`.
- Added `samples/FluxFlow.CompositionSample`, a pure in-memory source ->
  uppercase -> sink workflow.

## Boundary

Composition records named resource references but does not own broker clients,
stores, secrets, resource registration, file watching, YAML, live reload,
assembly scanning, reflection discovery, or engine projection.

Host or adapter DI still owns concrete clients, stores, secrets, connection
lifetime, and adapter-specific features.

## Docs

- Root README now states standalone-node-first as the default architecture.
- Docs entrypoints now list only existing samples and mark engine pages as
  optional advanced runtime guidance.
- Package authoring docs now say normal standalone nodes come first, with
  composition registration and engine modules as optional adapters.
- `FluxFlow.Engine` is described as optional legacy/advanced runtime, not the
  required composition path.

## Verification

- `dotnet build FluxFlow.sln -v minimal`
- `dotnet test tests\FluxFlow.Composition.Tests\FluxFlow.Composition.Tests.csproj --no-build -v minimal`
  - 12 passed
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-build -v minimal`
  - 33 passed
- `dotnet test FluxFlow.sln --no-build -v minimal`
  - passed across the solution
- `dotnet run --project samples\FluxFlow.CompositionSample\FluxFlow.CompositionSample.csproj`
  - printed `ALPHA` and `BETA`
- `graphify update . --force`
  - 8317 nodes, 12404 edges, 799 communities
