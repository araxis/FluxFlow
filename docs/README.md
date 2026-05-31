# FluxFlow.Engine Docs

These docs describe the standalone `FluxFlow.Engine` package.

Start with the neutral consumer sample in `samples/FluxFlow.SampleApp`. It shows
the intended application boundary: app-owned workspace metadata stays outside
the engine, and only executable resources and workflows are projected into
`ApplicationDefinition`.

## Contents

1. [Getting Started](01-getting-started.md)
2. [Definitions And Links](02-definitions-and-links.md)
3. [Node Authoring](03-node-authoring.md)
4. [Package Authoring](04-package-authoring.md)
5. [Hosting And Observability](05-hosting-and-observability.md)
6. [Workspace Projection](06-workspace-projection.md)

## Current Boundary

`FluxFlow.Engine` owns:

- executable definitions
- typed ports
- runtime graph building
- lifecycle coordination
- fanout and conditional links
- event and diagnostic aggregation
- node and package authoring helpers

Applications and component packages own:

- external protocols and clients
- storage
- UI metadata
- dashboards and designers
- test scenarios
- app-specific validation
- app-specific workspace files

Historical extraction notes live under `memory`.
