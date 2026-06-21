# Composition Hosting Layer

Date: 2026-06-21

## Decision

`FluxFlow.Composition.Hosting` is the optional DI and host-lifecycle bridge for
standalone compositions.

Core `FluxFlow.Composition` remains pure: DTOs, factory registry, validation,
linking, runtime lifecycle. Hosting owns `IServiceCollection`,
`IConfiguration`, `IHostedService`, and keyed-resource resolution helpers.

## Implemented

- Added `src/FluxFlow.Composition.Hosting`.
- Added `CompositionHostingOptions`:
  - `StartRuntimeWithHost`
  - `StopRuntimeWithHost`
  - `ThrowOnBuildFailure`
  - `StopTimeout`
- Added `ICompositionRuntimeHost` and `CompositionRuntimeHost`.
- Added `CompositionRuntimeHostedService` wrapper for `IHostedService`.
- Added `CompositionHostingException` carrying build diagnostics.
- Added `CompositionHostingBuilder` with:
  - `RegisterNodes(Action<CompositionNodeRegistry>)`
  - `Configure(Action<CompositionHostingOptions>)`
- Added definition sources:
  - `StaticCompositionDefinitionSource`
  - `ConfigurationCompositionDefinitionSource`
- Added `ICompositionNodeRegistryContributor` so packages can contribute node
  factories explicitly without scanning.
- Added `CompositionNodeFactoryContextResourceExtensions`:
  - `GetRequiredResourceKey(...)`
  - `GetRequiredResource<TResource>(...)`
  - `GetResource<TResource>(...)`
- Added service registration extensions:
  - `AddFluxFlowComposition(CompositionDefinition)`
  - `AddFluxFlowComposition(IConfiguration, sectionName)`
  - `AddFluxFlowCompositionSection(IConfiguration)`
  - `AddFluxFlowComposition(ICompositionDefinitionSource)`

## Boundary

Hosting resolves resources from keyed DI but does not create or configure
concrete resources. Adapter packages still own concrete clients, stores,
secrets, reconnect behavior, hosted client lifetime, and adapter-specific
options.

## Verification

- `dotnet build FluxFlow.sln -v minimal`
- `dotnet test tests\FluxFlow.Composition.Hosting.Tests\FluxFlow.Composition.Hosting.Tests.csproj --no-build -v minimal`
  - 5 passed
- `dotnet test tests\FluxFlow.Composition.Tests\FluxFlow.Composition.Tests.csproj --no-build -v minimal`
  - 12 passed
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-build -v minimal`
  - 33 passed
- `dotnet test FluxFlow.sln --no-build -v minimal`
  - passed across the solution
- `graphify update . --force`
  - 8456 nodes, 12587 edges, 814 communities
