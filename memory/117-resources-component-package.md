# Resources Package

Date: 2026-06-03

`FluxFlow.Components.Resources` now provides neutral named resource contracts.

## Decision

Create a small contracts and helper package instead of a runtime node package.

Hosts can use this package to declare resources, reference them by name, resolve
descriptors, and produce consistent diagnostics without coupling component
packages to a concrete lifecycle owner.

## Scope

- Adds `ResourceName` for validated resource names.
- Adds `ResourceReference` for named references with optional kind and
  attributes.
- Adds `ResourceDescriptor` for declared resources with optional kind, display
  fields, and metadata.
- Adds `IResourceLookup` and `ResourceLookupResult`.
- Adds `ResourceDiagnostic`, `ResourceDiagnosticCode`, and
  `ResourceDiagnosticSeverity`.
- Adds `ResourceDescriptorCatalog` for in-memory descriptor lookup.
- Adds `ResourceDiagnostics` helpers for validation, missing references,
  duplicate descriptors, unused descriptors, and kind mismatches.

## Boundary

- No runtime nodes.
- No concrete resource lifecycle.
- No concrete resource handle ownership.
- No renderer dependency.
- No host-specific storage or monitoring dependency.

## Package

- Package: `FluxFlow.Components.Resources`
- Version: `0.1.0-alpha.1`
- Public additions:
  - `ResourceName`
  - `ResourceReference`
  - `ResourceDescriptor`
  - `IResourceLookup`
  - `ResourceLookupResult`
  - `ResourceDiagnostic`
  - `ResourceDiagnosticCode`
  - `ResourceDiagnosticSeverity`
  - `ResourceDescriptorCatalog`
  - `ResourceDiagnostics`

## Verification

- Focused Resources tests passed: 9 tests.
- Full solution build passed in Release with 0 warnings.
- Full solution tests passed in Release.
- Package pack passed and produced
  `FluxFlow.Components.Resources.0.1.0-alpha.1.nupkg`.
- Release commit: `cedfa25`.
- Release tag: `components-resources-v0.1.0-alpha.1`.
- Release workflow run: `26898117265`, success.
- Main verification run: `26898105577`, success.
- Fresh public-feed restore/run smoke passed and returned
  `True:primary-profile:1`.
