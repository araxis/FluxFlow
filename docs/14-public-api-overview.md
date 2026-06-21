# Public API Overview

FluxFlow's default public surface is standalone-node-first:

- `FluxFlow.Nodes` for node authoring.
- `FluxFlow.Composition` for fluent/config composition of standalone nodes.
- `FluxFlow.Engine` for the optional advanced engine runtime.

## Composition

Namespace:

```text
FluxFlow.Composition
```

Main types:

- `CompositionDefinition`
- `WorkflowDefinition`
- `NodeDefinition`
- `LinkDefinition`
- `NodeReference`
- `PortReference`
- `CompositionDefinitionBuilder`
- `CompositionConfigurationLoader`
- `CompositionNodeRegistry`
- `CompositionNodeRegistration`
- `CompositionNodeFactoryContext`
- `ComposedNode`
- `CompositionPorts`
- `CompositionPortMetadata`
- `CompositionValidator`
- `CompositionRuntimeBuilder`
- `CompositionRuntime`
- `CompositionBuildResult`
- `ICompositionDefinitionSource`
- `ICompositionReloadPlanner`

Use these types when the host wants direct standalone-node composition from
fluent C# or `IConfiguration` JSON without depending on the engine.

## Composition Hosting

Namespace:

```text
FluxFlow.Composition.Hosting
```

Main types:

- `CompositionHostingOptions`
- `CompositionHostingBuilder`
- `ICompositionRuntimeHost`
- `CompositionRuntimeHost`
- `CompositionHostingException`
- `StaticCompositionDefinitionSource`
- `ConfigurationCompositionDefinitionSource`
- `ICompositionNodeRegistryContributor`
- `CompositionNodeFactoryContextResourceExtensions`

Use these types when a .NET host wants DI to load, build, start, stop, and
observe a composition runtime. Resource helpers resolve named node resource
references from keyed DI services; adapter packages still own the resources.

## HTTP Composition

Namespace:

```text
FluxFlow.Components.Http.Composition
```

Main types:

- `HttpCompositionNodeRegistryExtensions`
- `HttpCompositionNodeTypes`
- `HttpCompositionPortNames`
- `HttpCompositionResourceNames`

Use `RegisterHttpNodes()` from the optional
`FluxFlow.Components.Http.Composition` package when a composition host wants an
`http.client` node factory. The factory resolves a keyed `HttpClient` resource;
the host still owns client lifetime and transport policy.

## Mapping Composition

Namespace:

```text
FluxFlow.Components.Mapping.Composition
```

Main types:

- `MappingCompositionNodeRegistryExtensions`
- `MappingCompositionNodeTypes`
- `MappingCompositionPortNames`
- `MappingCompositionResourceNames`

Use `RegisterMapper<TInput,TOutput>()` from the optional
`FluxFlow.Components.Mapping.Composition` package when a composition host wants
closed generic `flow.mapper` node factories. The factory resolves a keyed
`IFlowExpressionEngine` resource; optional keyed context factory and clock
resources stay host-owned.

## Assertions Composition

Namespace:

```text
FluxFlow.Components.Assertions.Composition
```

Main types:

- `AssertionsCompositionNodeRegistryExtensions`
- `AssertionsCompositionNodeTypes`
- `AssertionsCompositionPortNames`
- `AssertionsCompositionResourceNames`

Use `RegisterAssertion<TInput>()` from the optional
`FluxFlow.Components.Assertions.Composition` package when a composition host
wants closed generic `flow.assert` node factories. The factory resolves a keyed
`IFlowExpressionEngine` resource; optional keyed typed context factory and clock
resources stay host-owned.

## Control Composition

Namespace:

```text
FluxFlow.Components.Control.Composition
```

Main types:

- `ControlCompositionNodeRegistryExtensions`
- `ControlCompositionNodeTypes`
- `ControlCompositionPortNames`
- `ControlCompositionResourceNames`

Use `RegisterFilter<TInput>()` and `RegisterWhen<TInput>()` from the optional
`FluxFlow.Components.Control.Composition` package when a composition host wants
closed generic `flow.filter` and `flow.when` node factories. The factories
resolve a keyed `IFlowExpressionEngine` resource; optional keyed typed context
factory and clock resources stay host-owned.

## Validation Composition

Namespace:

```text
FluxFlow.Components.Validation.Composition
```

Main types:

- `ValidationCompositionNodeRegistryExtensions`
- `ValidationCompositionNodeTypes`
- `ValidationCompositionPortNames`
- `ValidationCompositionResourceNames`

Use `RegisterJsonSchemaValidator<TInput>()` from the optional
`FluxFlow.Components.Validation.Composition` package when a composition host
wants closed generic `json.schema-validator` node factories. The factory binds
`JsonSchemaValidatorOptions`, compiles inline `schema` or `schemaPath` during
composition build, and resolves optional keyed typed selector and clock
resources through the host.

## Timers Composition

Namespace:

```text
FluxFlow.Components.Timers.Composition
```

Main types:

- `TimersCompositionNodeRegistryExtensions`
- `TimersCompositionNodeTypes`
- `TimersCompositionPortNames`
- `TimersCompositionResourceNames`

Use `RegisterTimerInterval()`, `RegisterTimerSchedule()`,
`RegisterTimerDelay<TInput>()`, `RegisterTimerThrottle<TInput>()`, and
`RegisterTimerDebounce<TInput>()` from the optional
`FluxFlow.Components.Timers.Composition` package when a composition host wants
timer source and transform node factories. The factories bind existing timer
settings and resolve optional keyed `TimeProvider` resources through the host.

## MQTT Composition

Namespace:

```text
FluxFlow.Components.Mqtt.Composition
```

Main types:

- `MqttCompositionNodeRegistryExtensions`
- `MqttCompositionNodeTypes`
- `MqttCompositionPortNames`
- `MqttCompositionResourceNames`

Use `RegisterMqttNodes()` from the optional
`FluxFlow.Components.Mqtt.Composition` package when a composition host wants
`mqtt.publish` and `mqtt.trigger` node factories. The factories resolve keyed
`IMqttPublisher` and `IMqttTriggerSource` resources; concrete MQTT adapters or
the host still own broker/client registration.

## Engine

`FluxFlow.Engine` exposes a small set of public namespaces. The goal for v1 is
that a host can author nodes, load executable definitions, build a runtime, and
observe lifecycle state without depending on internal runtime details.

## Hosting

Namespace:

```text
FluxFlow.Engine
```

Main types:

- `FlowApplicationHost`
- `FlowApplicationHostState`
- `FlowApplicationHostBuildResult`
- `FlowApplicationHostBuildError`
- `FlowApplicationConfigurationLoader`
- `FlowApplicationConfigurationException`

Use `FlowApplicationHost` when the host wants one object to own build, start,
stop, runtime diagnostics, and disposal.

Applications that use link `when` conditions must pass an `IFlowExpressionEngine`
to `FlowApplicationHost.Create(...)`.

## Definitions

Namespace:

```text
FluxFlow.Engine.Definitions
```

Main types:

- `ApplicationDefinition`
- `WorkflowDefinition`
- `NodeDefinition`
- `LinkDefinition`
- `ApplicationDefinitionJson`
- `ApplicationDefinitionValidator`
- `ApplicationDefinitionValidationResult`
- `ApplicationDefinitionValidationError`
- `ApplicationDefinitionValidationErrorCode`
- `NodeType`
- `NodeName`
- `WorkflowName`
- `PortName`
- `NodeAddress`
- `PortAddress`
- `WellKnownScopes`

Definitions are DTO-style contracts. Their dictionaries are intentionally
mutable for JSON loading and code-based authoring. Hosts can keep richer
workspace files, then project only executable resources and workflows into
`ApplicationDefinition`.

## Runtime

Namespace:

```text
FluxFlow.Engine.Runtime
```

Main types:

- `ApplicationRuntimeBuilder`
- `ApplicationRuntime`
- `Workflow`
- `RuntimeNode`
- `RuntimeNodeFactoryRegistry`
- `RuntimeNodeFactoryContext`
- `RuntimeNodeBuilder`
- `InputPort<T>`
- `OutputPort<T>`
- `ApplicationRuntimeBuildResult`
- `ApplicationRuntimeBuildError`
- `ApplicationRuntimeBuildErrorCode`
- `ApplicationRuntimeNodeStartException`
- `ApplicationState`
- `ApplicationStateChanged`
- `WorkflowState`
- `WorkflowStateChanged`
- `RuntimeFlowDiagnostic`
- `IFlowNodeRegistration`
- `FlowNodeRegistration`
- `IFlowNodeModule`
- `FlowNodeModule`

Use `ApplicationRuntimeBuilder` when the host wants to build the runtime
directly. Register every node factory explicitly through
`RuntimeNodeFactoryRegistry`.

Runtime build catches missing node types, missing ports, type mismatches,
unsupported conditional links, and missing expression engines before startup.

## Node Authoring

Namespace:

```text
FluxFlow.Engine.Components
```

Main types:

- `IFlowNode`
- `FlowNodeBase`
- `SourceFlowNode<TOutput>`
- `SinkFlowNode<TInput>`
- `TransformFlowNode<TInput,TOutput>`
- `MapFlowNode<TInput,TOutput>`
- `EventFlowNodeBase`
- `FlowNodeId`
- `FlowError`
- `FlowErrorCodes`
- `FlowEvent`
- `FlowDiagnostic`
- `FlowDiagnosticLevel`
- `IFlowDiagnosticSource`
- `IFlowEventSource`

Use these types for custom host nodes and reusable component package nodes.
Prefer the base classes when the node fits source, sink, transform, map, event,
error, or diagnostic patterns.

## Expression And Mapping Contracts

Namespace:

```text
FluxFlow.Engine.Mapping
```

Main types:

- `IFlowExpressionEngine`
- `FlowMapContext`
- `IFlowMapContextFactory<TInput>`
- `IFlowPredicate<TInput>`
- `ExpressionFlowPredicate<TInput>`
- `DelegateFlowPredicate<TInput>`
- `IFlowMapper<TInput,TOutput>`
- `DelegateFlowMapper<TInput,TOutput>`

The engine owns only the contracts. It does not ship a concrete expression
language. Hosts and component packages provide expression engines and context
factories.

## Stability Notes

For v1, the stable engine surface is the public API in these namespaces plus the
JSON shape documented in the definitions guide. Internal runtime helpers,
collectors, fanout queues, and cleanup helpers are not public extension points.

Next: [Engine Compatibility](15-engine-compatibility.md)
