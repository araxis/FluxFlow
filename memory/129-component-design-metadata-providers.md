# Component Design Metadata Providers

Date: 2026-06-05

## Decision

Reusable component packages now expose package-owned design metadata providers
using the neutral `FluxFlow.Components.Designer` contracts.

This keeps hosts from hand-maintaining component palette data, option metadata,
port labels, and defaults for package-owned runtime nodes.

## Host Composition Model

Packages own reusable metadata for their public node types:

- palette display names, categories, summaries, and icon keys
- option editor hints, defaults, choices, and required flags
- port labels, directions, ordering, grouping, and primary-port hints
- validation-facing option shape and documentation hints

Hosts compose package providers into a `ComponentDesignMetadataCatalog`, then
apply host-specific behavior, localization, rendering, resource pickers, or
workflow-editor overrides outside the package descriptors.

Host applications should not duplicate package descriptors just to show reusable
nodes in palettes, editors, validation views, or generated documentation.

## Added Providers

- Assertions
- Control
- File system
- HTTP
- Mapping
- Metrics
- MQTT
- Observability
- Payloads
- Routing
- Serialization
- Sessions
- Sources
- State
- Storage
- Timers
- Validation

Each package provider implements `IComponentDesignMetadataProvider` and returns
`ComponentDesignMetadata` for the public component type constants in that
package.

## Release Plan

The provider classes are package public surface area.

- Publish `FluxFlow.Components.Designer` `1.0.1` as a README/docs maintenance
  patch for provider composition guidance.
- Publish affected runtime component packages as `1.1.0` because they add
  package-owned provider classes and a Designer dependency.
- Keep `FluxFlow.Engine` at `1.0.1`; this work does not change engine public
  APIs, runtime behavior, definitions, or JSON shape.
- Do not republish the existing stable `1.0.0` package versions for this work.

## Verification

- Added designer tests that compose all package providers into one
  `ComponentDesignMetadataCatalog`.
- Added coverage tests that all listed public component type constants have
  package metadata.
- Verified host catalog composition can consume package providers directly,
  including serialization transforms, while leaving host-only behavior outside
  reusable package metadata.
- Verified focused designer metadata tests:
  - `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --configuration Release`
- Verified release guard tests:
  - `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --configuration Release`
- Verified full Release solution:
  - `dotnet build FluxFlow.sln --configuration Release`
  - `dotnet test FluxFlow.sln --configuration Release --no-build`
