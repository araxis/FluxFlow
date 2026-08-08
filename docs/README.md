# FluxFlow Docs

FluxFlow is standalone-node-first. Use `FluxFlow.Nodes` for transport-neutral
exact content, error contracts, and typed node authoring; add
`FluxFlow.Composition` for portable JSON or typed compiled-C# application
authoring, addressing, and link compilation. Add `FluxFlow.Engine` when a .NET host should activate
revisions through DI, stable ports, and adapter-owned keyed resources.
Convert retired workflows/nodes/links documents outside normal startup before
loading them; the runtime accepts only the canonical document shape and names.

## Current Samples

- `samples/FluxFlow.CompositionSample`: typed code-first application contracts, handles, `ConnectTo`, and direct in-memory hosting.
- `samples/FluxFlow.DurabilityOperationsSample`: one local durable cycle with host-owned BCL diagnostics and explicit persisted-status snapshots.
- `samples/FluxFlow.FluentSample`: the concise node-instance fluent facade over
  the canonical definition and Engine, including branching and fan-in.
- `samples/FluxFlow.MqttCompositionSample`: MQTT-shaped hosted composition with an in-memory logical client controller.
- `samples/FluxFlow.HttpTriggerSample`: host-owned HTTP trigger wiring without the engine.
- `samples/FluxFlow.SampleApp`: advanced typed application builder with C# predicates, cross-scope handles, and Events fan-in.
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
15. [Engine Migration Boundaries](15-engine-compatibility.md)
17. [Component Coverage Matrix](17-component-coverage-matrix.md)
18. [Designer Host Layer](18-designer-host-layer.md)
19. [vNext Runtime Architecture](19-vnext-runtime-architecture.md)
20. [Flow Data Contracts](20-flow-data-contracts.md)
21. [Component Type Names](21-component-type-names.md)
22. [Canonical Migration](22-canonical-vnext-migration.md)
23. [Major Surface Reset](23-engine-2-to-3-migration.md)
24. [Reliable In-Process Delivery](24-reliable-in-process-delivery.md)
25. [Optional Durable Inputs](25-durable-inputs.md)
26. [SQL-File Durable Inputs](26-sql-file-durable-inputs.md)
27. [Optional Durable Output Capture](27-durable-output-capture.md)
28. [SQL-File Durable Outputs](28-sql-file-durable-outputs.md)
29. [Optional Durable Output Delivery](29-durable-output-delivery.md)
30. [Durable Output Dead-Letter Operations](30-durable-output-dead-letter-operations.md)
31. [Networked Relational Durable-Output Feasibility](31-networked-relational-durable-output-feasibility.md)
32. [T-SQL Durable Outputs](32-tsql-durable-outputs.md)
33. [Durable-Input Workflow Completion](33-durable-input-workflow-completion.md)
34. [T-SQL Durable Inputs](34-tsql-durable-inputs.md)
35. [Durability Operational Status](35-durability-operational-status.md)
36. [Durable Terminal Retention](36-durable-terminal-retention.md)
37. [Durable Output Lease Renewal](37-durable-output-lease-renewal.md)
38. [Release Validation](38-release-validation.md)
39. [Typed Code-First Application Authoring](39-typed-code-first-authoring.md)
40. [Unified Component Contracts](40-unified-component-contracts.md)
41. [End-To-End Code-First Simplification](41-end-to-end-code-first-simplification.md)
42. [Application Health Readiness](42-application-health-readiness.md)
43. [Performance, Concurrency, And Lifetime Baseline](43-performance-concurrency-lifetime-baseline.md)
44. [Release-Candidate Consolidation](44-release-candidate-consolidation.md)

Retired documents require an external, one-time conversion. Current runtime
guidance uses only the canonical application and component model.
