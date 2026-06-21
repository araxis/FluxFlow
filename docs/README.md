# FluxFlow Docs

FluxFlow is standalone-node-first. Start with `FluxFlow.Nodes`; add
`FluxFlow.Composition` when you want fluent C# or `IConfiguration` JSON to build
and link standalone nodes. Add `FluxFlow.Composition.Hosting` when a .NET host
should build/start the composition through DI and resolve adapter-owned keyed
resources. Use `FluxFlow.Engine` only when the older `ApplicationDefinition`
runtime is the right fit for a host.

## Current Samples

- `samples/FluxFlow.CompositionSample`: pure in-memory standalone composition.
- `samples/FluxFlow.MqttCompositionSample`: MQTT-shaped hosted composition with in-memory adapter resources.
- `samples/FluxFlow.HttpTriggerSample`: host-owned HTTP trigger wiring without the engine.
- `samples/FluxFlow.SampleApp`: optional advanced engine runtime sample.
- `samples/FluxFlow.ComponentPackageTemplate`: copyable standalone component package shape.

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

Pages 2 and later still describe engine-era APIs where named. Treat those as
optional advanced runtime guidance, not the default component-package contract.
