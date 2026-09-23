# Core and Engine execution boundary

The project consolidation is currently at 11 production projects and 29 solution
projects. Engine cannot be deleted as an unused wrapper: it owns transactional
definition updates and stable port generations that the direct Core graph does
not expose. Moving its files would reduce project count while retaining two
application models. This step starts with a shared execution responsibility.

## What is already shared

Both paths use Core's FlowOutput for bounded output delivery and fanout,
FlowRouteSelection for ordinary/fallback selection, and FlowDependencyOrder for
shutdown ordering. Definitions use the same component factory registrations,
message/value types and condition compiler. Component processing diagnostics
remain in the common node implementation.

## What still differs

| Responsibility | Direct Core | Engine |
| --- | --- | --- |
| Application ownership | FlowApplication owns a graph frozen at start | FluxFlowApplication owns active and candidate revisions |
| Port identity | Endpoints point at graph component blocks | ApplicationPortRuntime provides stable addresses and swaps revision targets |
| Construction | ApplicationConstructionExtensions adds instances to workflows | ApplicationRuntimePreparation prepares resources, instances, links and a port revision |
| Retirement | FlowExecution stops and drains the graph | ApplicationRuntimeDrainPlan drains revision inputs/outputs before resource retirement |
| Condition failure | FlowRouter reports isolated failure or faults delivery | ApplicationOutputPort also emits rejection diagnostics with message IDs |

Relevant source: src/FluxFlow.Core/Application/FlowApplication.cs, FlowRouter.cs and
FlowExecution.cs; src/FluxFlow.Engine/FluxFlowApplication.cs,
Hosting/ApplicationRuntimePreparation.cs, Hosting/ApplicationRuntimeDrainPlan.cs,
Hosting/ApplicationRuntimePortGeneration.cs and Ports/ApplicationOutputPort.cs.
These differences require behavioral review; they are not all redundant layers.

## Completed change

Four callers independently created the same condition variables: DocumentLinks,
PortableApplicationExtensions, ApplicationConstructionExtensions and
ApplicationOutputPort. They now use an internal FlowMapContext.ForMessage method
on the existing context type. No new class, interface, public API, cache or
serialization layer was introduced. input/payload reference the original value,
and message references the original envelope. Each evaluation gets its own
variables, preserving isolation and lazy JSON conversion. Error policy and
diagnostic reporting stay with the existing callers.

Two contract tests verify lazy payload handling and variable isolation; each runs
on net8.0/net10.0. Existing JSON, native predicate, factory construction, hosted
delivery, fallback, rejection, revision, cancellation, disposal and persistence
suites provide broader regression coverage. Full local results are recorded in
artifacts/runtime-boundary-20260915/verification.json. Live SQL Server integration
and a new package-only consumer run are outside this internal change's validation.

## Next bounded step

Review component construction and cleanup ownership across the two paths. The
single-use ApplicationRuntimeComponentActivator is one potential removable
indirection; preserving cancellation, partial-construction cleanup and ownership
checks takes precedence over sharing a loop. Then characterize a complete workflow
under both application APIs before replacing either execution lifetime. Keep
transactional update and stable-address behavior. HealthChecks remains separate
until the Core/Engine dependency boundary is settled.
