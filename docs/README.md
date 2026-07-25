# FluxFlow Docs

FluxFlow is standalone-node-first. Use `FluxFlow.Data` for transport-neutral
values, content, and result contracts, then start node authoring with
`FluxFlow.Nodes`; add
`FluxFlow.Composition` for the canonical application document, addressing, and
static link compilation. Add `FluxFlow.Composition.Hosting` and
`FluxFlow.Engine` when a .NET host should activate revisions through DI,
stable ports, and adapter-owned keyed resources. Convert retired
workflows/nodes/links documents explicitly before normal startup.

## Current Samples

- `samples/FluxFlow.CompositionSample`: canonical application hosting with an in-memory component graph.
- `samples/FluxFlow.FluentSample`: the same pipeline built with the type-safe fluent DSL, plus a branching/fan-in example.
- `samples/FluxFlow.MqttCompositionSample`: MQTT-shaped hosted composition with an in-memory logical client controller.
- `samples/FluxFlow.HttpTriggerSample`: host-owned HTTP trigger wiring without the engine.
- `samples/FluxFlow.SampleApp`: optional advanced engine runtime sample.
- `samples/FluxFlow.ComponentPackageTemplate`: copyable standalone component package shape.
- `samples/FluxFlow.DesignerHost`: headless Designer host-model layer projecting
  component design metadata into palette, inspector, and resource picker view
  models (no UI, no resource ownership).
- `samples/FluxFlow.DesignerApp`: Blazor WebAssembly + MudBlazor renderer over
  the Designer host-model layer — component palette and option/resource
  inspector driven by the real package metadata catalog.

## Contents

1. [Getting Started](01-getting-started.md)
2. [Definitions And Links](02-definitions-and-links.md)
3. [Node Authoring](03-node-authoring.md)
4. [Package Authoring](04-package-authoring.md)
5. [Hosting And Observability](05-hosting-and-observability.md)
6. [Workspace Projection](06-workspace-projection.md)
7. [Validation And Errors](07-validation-and-errors.md)
8. [Runtime States](08-runtime-states.md)
9. [JSON Conversion](09-json-conversion.md)
10. [Expression Mapping](10-expression-mapping.md)
11. [Package Versioning](11-package-versioning.md)
12. [Component Composition](12-component-composition.md)
13. [Storage Host Adapters](13-storage-host-adapters.md)
14. [Public API Overview](14-public-api-overview.md)
15. [Engine Compatibility](15-engine-compatibility.md)
16. [Migration 0.5 To 0.6](16-migration-0.5-to-0.6.md)
17. [Component Coverage Matrix](17-component-coverage-matrix.md)
18. [Designer Host Layer](18-designer-host-layer.md)
19. [vNext Runtime Architecture](19-vnext-runtime-architecture.md)
20. [Flow Data Contracts](20-flow-data-contracts.md)
21. [Component Type Names](21-component-type-names.md)
22. [Canonical vNext Migration](22-canonical-vnext-migration.md)

Retired Composition documents have an explicit conversion boundary. Older
Engine surfaces are labeled where they remain pending their separately verified
vNext removal.
