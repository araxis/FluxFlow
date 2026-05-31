# FluxFlow Engine Adoption Findings

## Context

FluxMQ is preparing to replace its local workflow-engine layer with `FluxFlow.Engine`.
During the migration spike, the main architectural concern was not API syntax. The concern is ownership:
`FluxFlow.Engine` should stay a generic executable workflow engine, while FluxMQ should own workspace artifacts such as dashboards and test scenarios.

## Main Finding

`FluxFlow.Engine.ApplicationDefinition` should not contain FluxMQ workspace concepts.

Recommended engine definition:

```text
ApplicationDefinition
- resources
- workflows
```

Recommended FluxMQ workspace document:

```text
FluxMqApplicationDefinition
- resources
- workflows
- dashboards
- tests
```

FluxMQ can then project the executable part into the engine:

```text
FluxMqApplicationDefinition.ToEngineDefinition()
=> FluxFlow.Engine.ApplicationDefinition
   - resources
   - workflows
```

This avoids a duplicated-definition problem during migration and keeps the core engine independent from FluxMQ UI and test-runner concerns.

## Changes Requested In FluxFlow.Engine

1. Remove `Tests` from `FluxFlow.Engine.Definitions.ApplicationDefinition`.

   Tests are not executable workflow structure. They are a consumer of the runtime and event stream.

2. Remove scenario/test validation from `ApplicationDefinitionValidator`.

   The engine validator should validate only:
   - resource names and node definitions
   - workflow names and node definitions
   - links, source scopes, source nodes, source ports, duplicate links

3. Remove scenario/test runner APIs from the core host.

   `FlowApplicationHost` should build, start, stop, dispose, and expose runtime state/diagnostics. It should not own `RunScenarioAsync`, default scenario runners, or scenario step service creation.

4. Move generic testing pieces out of the engine package, or temporarily leave them out.

   If reusable test primitives are still desired, prefer a separate package/layer such as:

   ```text
   FluxFlow.Testing
   - ScenarioDefinition
   - ScenarioRunner
   - ScenarioStepRunnerRegistry
   - expect.event
   - event journal / observer helpers
   ```

   This package can depend on `FluxFlow.Engine`; the engine should not depend on testing.

5. Keep dashboard concepts completely out of FluxFlow.

   Dashboard layout, widgets, widget filters, widget validation, and dashboard JSON converters should remain in FluxMQ.

6. Keep protocol-specific concepts out of FluxFlow.

   The engine should not contain MQTT, retain/QoS, broker, topic, session replay, dashboard, or FluxMQ-specific node type constants. Those belong to FluxMQ.

7. Keep generic runtime diagnostics in FluxFlow.

   Runtime diagnostics, event collection, node address mapping, lifecycle state, build errors, cleanup failures, and node type metadata are generic and useful for FluxMQ UI projections.

8. Keep generic mapping abstractions in FluxFlow.

   `IFlowExpressionEngine`, mapping context, predicate/mapper contracts, and generic expression engines are reasonable engine-level utilities if they remain protocol-neutral.

9. Keep resources in the engine, but define them as runtime-scoped shared nodes.

   FluxMQ connections can be represented as resources when projected into the engine, but the engine should not know what a connection means.

## Suggested Package Shape

```text
FluxFlow.Engine
- Definitions: resources, workflows, nodes, links, ports
- Runtime: builder, runtime, workflow state, host lifecycle
- Components: node contracts, flow events, flow errors, diagnostics, fanout helpers
- Mapping: protocol-neutral mapper/predicate/expression contracts
```

Optional later:

```text
FluxFlow.Testing
- Scenario definitions
- Scenario runner
- Generic event expectations
- Test result models
```

FluxMQ should own:

```text
FluxMQ
- FluxMqApplicationDefinition
- Dashboards
- MQTT node factories
- MQTT event type constants
- MQTT test steps
- MQTT scenario validation
- UI/CLI workspace loading and projection into FluxFlow.Engine
```

## Migration Impact On FluxMQ

After FluxFlow is cleaned up, FluxMQ migration becomes much simpler:

1. Introduce `FluxMqApplicationDefinition`.
2. Move dashboard definitions and validation into FluxMQ.
3. Keep tests in FluxMQ or move generic pieces to a separate testing package.
4. Project `resources/workflows` into `FluxFlow.Engine.ApplicationDefinition`.
5. Replace `FluxMq.Pipeline` runtime/types with `FluxFlow.Engine`.
6. Delete the old local pipeline project once consumers are moved.

## Current Migration Readiness

The migration is feasible, but not as a direct namespace/package swap.

The biggest blocker to a clean migration is the current overlap between:

```text
FluxMQ workspace document
FluxFlow engine document
test runner concepts
dashboard concepts
```

Resolve that boundary first in FluxFlow, then FluxMQ can adopt the engine in smaller, safer slices.

## Final Recommendation

Before continuing the FluxMQ migration, update `FluxFlow.Engine` so its public definition model is engine-only:

```text
resources + workflows
```

Move or remove testing from the core engine, keep dashboards entirely in FluxMQ, and keep all MQTT-specific behavior in FluxMQ. This gives us a clean dependency direction:

```text
FluxMQ workspace/test/dashboard layer
        depends on
FluxFlow.Engine runtime layer
```

That is the shape we should migrate toward.
