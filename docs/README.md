# FluxFlow.Engine Docs

These docs describe the standalone `FluxFlow.Engine` package.

Start with the neutral consumer sample in `samples/FluxFlow.SampleApp`. It shows
the intended application boundary: app-owned workspace metadata stays outside
the engine, and only executable resources and workflows are projected into
`ApplicationDefinition`.

Then run `samples/FluxFlow.MappingControlSample` to see package composition with
host-owned source/sink nodes and reusable mapping/control nodes.

Add `FluxFlow.Components.Validation` when a workflow needs package-owned JSON
schema validation with host-owned value selection.

Add `FluxFlow.Components.FileSystem` when a workflow needs package-owned file
system operations such as `file.write`, `file.read`, `file.watch`, and
`directory.enumerate`.

Add `FluxFlow.Components.Observability` when a workflow needs neutral
structured log entries, metrics snapshots, or counter snapshots.

Add `FluxFlow.Components.Timers` when a workflow needs package-owned interval
ticks, cron schedule ticks, or typed pass-through delays.

Run `samples/FluxFlow.MqttCompositionSample` to see MQTT package integration
through a host-owned in-memory adapter.

Use `samples/FluxFlow.ComponentPackageTemplate` as the copyable starting point
for a new component package.

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

## Current Boundary

`FluxFlow.Engine` owns:

- executable definitions
- typed ports
- runtime graph building
- lifecycle coordination
- fanout and conditional links
- event and diagnostic aggregation
- structured validation and build errors
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
