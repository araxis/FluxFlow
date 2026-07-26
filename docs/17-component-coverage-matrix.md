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
| `FluxFlow.Coordination` | bounded generic pending-exchange coordination | yes | yes | n/a | n/a | aligned |
| `FluxFlow.Resilience` | transport-neutral retry policy, schedules, and execution | yes | yes | n/a | n/a | aligned |
| `FluxFlow.Composition` | canonical definitions, links, component contracts, code-first lifecycle, and explicit legacy document migration | yes | yes | n/a | n/a | aligned |
| `FluxFlow.Composition.Hosting` | canonical application revisions and immutable DI snapshots | yes | yes | n/a | n/a | aligned |
| `FluxFlow.Mapping` | expression and mapping contracts | yes | yes | n/a | n/a | aligned |
| `FluxFlow.Engine` | canonical runtime assembler, stable ports, system signals, and explicit legacy JSON migration | yes | yes | n/a | n/a | aligned |

## Component Node Families

| Family | Runtime package | Tests | Composition package | Composition tests | Designer metadata provider | Status |
|--------|-----------------|-------|---------------------|-------------------|----------------------------|--------|
| Resilience | `FluxFlow.Components.Resilience` | yes | `FluxFlow.Components.Resilience.Composition` | yes | yes | typed `flow.retry` with TraceId lineage, attempt-safe feedback, and value-or-error output |
| MQTT | `FluxFlow.Components.Mqtt` | yes | `FluxFlow.Components.Mqtt.Composition` | yes | yes | canonical controller/transport/result contracts consolidated |
| HTTP client | `FluxFlow.Components.Http` | yes | `FluxFlow.Components.Http.Composition` | yes | yes | typed request/response contract with in-band errors |
| Mapping | `FluxFlow.Components.Mapping` | yes | `FluxFlow.Components.Mapping.Composition` | yes | yes | generic typed mapper plus explicit schema-less JSON registration |
| Control | `FluxFlow.Components.Control` | migration-only | `FluxFlow.Components.Control.Composition` | migration-only | no | structural Filter and When removed; canonical link conditions replace them |
| Assertions | `FluxFlow.Components.Assertions` | yes | `FluxFlow.Components.Assertions.Composition` | yes | yes | generic typed assertion plus explicit schema-less JSON registration |
| Sources | `FluxFlow.Components.Sources` | yes | `FluxFlow.Components.Sources.Composition` | yes | yes | typed generated values and sequence-item source contracts |
| Routing | `FluxFlow.Components.Routing` | yes | `FluxFlow.Components.Routing.Composition` | yes | yes | typed Window, Correlation, and Join nodes plus explicit JSON specializations |
| Validation | `FluxFlow.Components.Validation` | yes | `FluxFlow.Components.Validation.Composition` | yes | yes | explicit `JsonElement` schema boundary with typed validation outcomes |
| File system | `FluxFlow.Components.FileSystem` | yes | `FluxFlow.Components.FileSystem.Composition` | yes | yes | aligned |
| Observability | `FluxFlow.Components.Observability` | yes | `FluxFlow.Components.Observability.Composition` | yes | yes | generic typed Counter, Logger, and Metrics nodes with JSON specializations |
| Timers | `FluxFlow.Components.Timers` | yes | `FluxFlow.Components.Timers.Composition` | yes | yes | typed tick sources and generic pass-through transforms |
| Payloads | `FluxFlow.Components.Payloads` | yes | `FluxFlow.Components.Payloads.Composition` | yes | yes | exact-content inspection without hidden decoding |
| Serialization | `FluxFlow.Components.Serialization` | yes | `FluxFlow.Components.Serialization.Composition` | yes | yes | explicit `FlowContent`, JSON, text, and Base64 conversions |
| Metrics | `FluxFlow.Components.Metrics` | yes | `FluxFlow.Components.Metrics.Composition` | yes | yes | typed sample-to-snapshot value-or-error contract |
| Projections | `FluxFlow.Components.Projections` | yes | `FluxFlow.Components.Projections.Composition` | yes | yes | typed event-to-snapshot value-or-error contract |
| Expectations | `FluxFlow.Components.Expectations` | yes | `FluxFlow.Components.Expectations.Composition` | yes | yes | typed projection-event expectation outcomes |
| Sessions | `FluxFlow.Components.Sessions` | yes | `FluxFlow.Components.Sessions.Composition` | yes | yes | exact-content records and typed query outcomes |
| State | `FluxFlow.Components.State` | yes | `FluxFlow.Components.State.Composition` | yes | yes | generic typed state reduction plus explicit JSON specialization |
| Storage | `FluxFlow.Components.Storage` | yes | `FluxFlow.Components.Storage.Composition` | yes | yes | typed requests/outcomes over host-owned stores |

## Adapter And Support Packages

| Package | Role | Tests | README | Composition adapter | Designer metadata | Status |
|---------|------|-------|--------|---------------------|-------------------|--------|
| `FluxFlow.Components.Http.AspNetCore` | host-owned inbound HTTP trigger integration | yes | yes | intentional | intentional | adapter-owned integration |
| Concrete MQTT transport adapters (2 packages) | concrete MQTT transport adapters | yes | yes | intentional | intentional | provider sessions only; core owns policy and lifecycle |
| `FluxFlow.Components.Configuration` | resource/secret configuration validation support | yes | yes | intentional | intentional | support-only |
| `FluxFlow.Components.Expressions` | expression and context registry support | yes | yes | intentional | intentional | support-only |
| `FluxFlow.Components.Journal` | journal store contracts and in-memory store | yes | yes | intentional | intentional | support-only |
| `FluxFlow.Components.Resources` | resource descriptor and lookup support | yes | yes | intentional | intentional | support-only |
| `FluxFlow.Components.Secrets` | secret descriptor and resolution support | yes | yes | intentional | intentional | support-only |
| `FluxFlow.Components.Designer` | neutral metadata plus canonical application editing projections | yes | yes | n/a | provider contract | support-only |
| `FluxFlow.Components.RequestReply` | transport request/reply correlation support | yes | yes | intentional | intentional | support-only by current decision |
| `FluxFlow.Components.Storage.FileSystem` | concrete storage backend | yes | yes | intentional | intentional | backend adapter |
| `FluxFlow.Components.Storage.SqlFile` | concrete storage backend | yes | yes | intentional | intentional | backend adapter |

## Enforced Rules

Release tests currently enforce these consistency rules:

- every source package is listed in `eng/packages.json`
- every manifest package is mentioned in the public API overview
- every manifest package has release metadata, changelog coverage, and a
  package-local README packed as `README.md`
- every package README starts with an H1 matching its package id
- package binary compatibility preflight is available for release-readiness
  checks against published package baselines
- non-composition component packages stay free of `FluxFlow.Engine`,
  `FluxFlow.Composition`, `FluxFlow.Composition.Hosting`, and
  `FluxFlow.Components.Designer`
- support-only packages stay free of node runtime references and node classes
- normal component package READMEs document their composition boundary
- package READMEs have been reviewed for clear examples and host-owned resource
  boundary wording after the Designer metadata hint and MQTT adapter releases
- composition packages expose registry methods, node-type constants, port and
  resource constants, Designer metadata providers, and package docs
- Designer metadata validates, is catalog-ready, exposes neutral host-owned
  resource picker hint helpers, and stays aligned with registry metadata, bound
  options, required resources, ports, defaults, and enum choices
- repeated option and host-owned resource shapes use small Designer factories;
  component-specific metadata remains explicit and catalog-equivalent
- Designer persistence projects the canonical flat application model, preserves
  link declaration sides, exposes resource namespaces/references, and reuses
  Composition addressing and diagnostics

## Next Isolated Plans

Future work should be explicit and narrow. Good candidates:

- implement renderer UI over the completed headless Designer host models and
  canonical persistence contracts as a separate bounded pass per
  `docs/18-designer-host-layer.md`
- plan hot reload in `FluxFlow.Composition` as a dedicated lifecycle feature
- revisit `FluxFlow.Components.RequestReply` only if a real composition node
  surface is explicitly needed
- implement `control.gate` as a separate Control-family pass with Input, Open,
  Close, Output, Events, and the agreed bounded drop-oriented queue behavior;
  it does not belong in Coordination or Resilience
