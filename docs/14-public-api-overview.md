# Public API Overview

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
