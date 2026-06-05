# Component Design Metadata Provider Release

Date: 2026-06-05

## Decision

Release the package-owned component design metadata provider capability as a
component-package wave.

- `FluxFlow.Engine` stays at `1.0.1`.
- `FluxFlow.Components.Designer` moves to `1.0.1` as a README/docs maintenance
  patch for provider composition guidance.
- Runtime component packages that added package-owned
  `IComponentDesignMetadataProvider` implementations move to `1.1.0`.

## Affected Runtime Packages

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

## Invariants

- Runtime behavior is unchanged.
- Node contracts are unchanged.
- Definition and JSON shape are unchanged.
- Existing registration APIs are unchanged.
- Host-specific rendering and behavior overrides stay outside package metadata.

## Release Metadata

- Project versions now match the planned package versions.
- Package release notes describe the provider capability and unchanged runtime
  behavior.
- `CHANGELOG.md` has matching sections for Designer `1.0.1` and affected
  runtime packages `1.1.0`.
