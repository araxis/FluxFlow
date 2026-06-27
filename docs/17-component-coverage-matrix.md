# Component Coverage Matrix

This page is the current standalone-first coverage checkpoint. It separates
normal component node families from support packages and advanced runtime
packages so future work can be planned as narrow passes instead of restarting a
component-by-component loop.

Status values:

- `yes`: package/project/test/docs artifact exists and is covered by current
  release conventions.
- `n/a`: not applicable for that package role.
- `intentional`: intentionally absent because the package is support-only,
  adapter-only, or advanced runtime infrastructure.

## Core And Runtime Packages

| Package | Role | Tests | README | Composition adapter | Designer metadata | Status |
|---------|------|-------|--------|---------------------|-------------------|--------|
| `FluxFlow.Nodes` | standalone node kit | yes | yes | n/a | n/a | aligned |
| `FluxFlow.Composition` | standalone composition DTOs, validation, build, and runtime | yes | yes | n/a | n/a | aligned |
| `FluxFlow.Composition.Hosting` | DI and hosted composition bridge | yes | yes | n/a | n/a | aligned |
| `FluxFlow.Mapping` | expression and mapping contracts | yes | yes | n/a | n/a | aligned |
| `FluxFlow.Engine` | optional advanced `ApplicationDefinition` runtime | yes | yes | intentional | intentional | optional legacy/advanced path |

## Component Node Families

| Family | Runtime package | Tests | Composition package | Composition tests | Designer metadata provider | Status |
|--------|-----------------|-------|---------------------|-------------------|----------------------------|--------|
| MQTT | `FluxFlow.Components.Mqtt` | yes | `FluxFlow.Components.Mqtt.Composition` | yes | yes | aligned |
| HTTP client | `FluxFlow.Components.Http` | yes | `FluxFlow.Components.Http.Composition` | yes | yes | aligned |
| Mapping | `FluxFlow.Components.Mapping` | yes | `FluxFlow.Components.Mapping.Composition` | yes | yes | aligned |
| Control | `FluxFlow.Components.Control` | yes | `FluxFlow.Components.Control.Composition` | yes | yes | aligned |
| Assertions | `FluxFlow.Components.Assertions` | yes | `FluxFlow.Components.Assertions.Composition` | yes | yes | aligned |
| Sources | `FluxFlow.Components.Sources` | yes | `FluxFlow.Components.Sources.Composition` | yes | yes | aligned |
| Routing | `FluxFlow.Components.Routing` | yes | `FluxFlow.Components.Routing.Composition` | yes | yes | aligned |
| Validation | `FluxFlow.Components.Validation` | yes | `FluxFlow.Components.Validation.Composition` | yes | yes | aligned |
| File system | `FluxFlow.Components.FileSystem` | yes | `FluxFlow.Components.FileSystem.Composition` | yes | yes | aligned |
| Observability | `FluxFlow.Components.Observability` | yes | `FluxFlow.Components.Observability.Composition` | yes | yes | aligned |
| Timers | `FluxFlow.Components.Timers` | yes | `FluxFlow.Components.Timers.Composition` | yes | yes | aligned |
| Payloads | `FluxFlow.Components.Payloads` | yes | `FluxFlow.Components.Payloads.Composition` | yes | yes | aligned |
| Serialization | `FluxFlow.Components.Serialization` | yes | `FluxFlow.Components.Serialization.Composition` | yes | yes | aligned |
| Metrics | `FluxFlow.Components.Metrics` | yes | `FluxFlow.Components.Metrics.Composition` | yes | yes | aligned |
| Projections | `FluxFlow.Components.Projections` | yes | `FluxFlow.Components.Projections.Composition` | yes | yes | aligned |
| Expectations | `FluxFlow.Components.Expectations` | yes | `FluxFlow.Components.Expectations.Composition` | yes | yes | aligned |
| Sessions | `FluxFlow.Components.Sessions` | yes | `FluxFlow.Components.Sessions.Composition` | yes | yes | aligned |
| State | `FluxFlow.Components.State` | yes | `FluxFlow.Components.State.Composition` | yes | yes | aligned |
| Storage | `FluxFlow.Components.Storage` | yes | `FluxFlow.Components.Storage.Composition` | yes | yes | aligned |

## Adapter And Support Packages

| Package | Role | Tests | README | Composition adapter | Designer metadata | Status |
|---------|------|-------|--------|---------------------|-------------------|--------|
| `FluxFlow.Components.Http.AspNetCore` | host-owned inbound HTTP trigger integration | yes | yes | intentional | intentional | adapter-owned integration |
| Concrete MQTT client adapters (2 packages) | concrete MQTT client adapters | yes | yes | intentional | intentional | adapter-owned resource setup |
| `FluxFlow.Components.Configuration` | resource/secret configuration validation support | yes | yes | intentional | intentional | support-only |
| `FluxFlow.Components.Expressions` | expression and context registry support | yes | yes | intentional | intentional | support-only |
| `FluxFlow.Components.Journal` | journal store contracts and in-memory store | yes | yes | intentional | intentional | support-only |
| `FluxFlow.Components.Resources` | resource descriptor and lookup support | yes | yes | intentional | intentional | support-only |
| `FluxFlow.Components.Secrets` | secret descriptor and resolution support | yes | yes | intentional | intentional | support-only |
| `FluxFlow.Components.Designer` | neutral design metadata contracts and catalog | yes | yes | n/a | provider contract | support-only |
| `FluxFlow.Components.RequestReply` | transport request/reply correlation support | yes | yes | intentional | intentional | support-only by current decision |
| `FluxFlow.Components.Storage.FileSystem` | concrete storage backend | yes | yes | intentional | intentional | backend adapter |
| `FluxFlow.Components.Storage.SqlFile` | concrete storage backend | yes | yes | intentional | intentional | backend adapter |

## Enforced Rules

Release tests currently enforce these consistency rules:

- every source package is listed in `eng/packages.json`
- every manifest package is mentioned in the public API overview
- every manifest package has release metadata, changelog coverage, and a
  package-local README packed as `README.md`
- non-composition component packages stay free of `FluxFlow.Engine`,
  `FluxFlow.Composition`, `FluxFlow.Composition.Hosting`, and
  `FluxFlow.Components.Designer`
- support-only packages stay free of node runtime references and node classes
- normal component package READMEs document their composition boundary
- composition packages expose registry methods, node-type constants, port and
  resource constants, Designer metadata providers, and package docs
- Designer metadata validates, is catalog-ready, and stays aligned with
  registry metadata, bound options, required resources, ports, defaults, and
  enum choices

## Next Isolated Plans

Future work should be explicit and narrow. Good candidates:

- run a full package README pass for clarity and examples without changing APIs
- expand the public API baseline into binary compatibility checks only if that
  becomes a release requirement
- plan a Designer UI host or resource-picker layer outside component packages
- plan hot reload in `FluxFlow.Composition` as a dedicated lifecycle feature
- revisit `FluxFlow.Components.RequestReply` only if a real composition node
  surface is explicitly needed
