# FluxFlow.Components.Control

Reusable expression-driven control components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `flow.filter` | `Input` -> `Output` | Emits only input values that match an expression. |
| `flow.when` | `Input` -> `WhenTrue` / `WhenFalse` | Routes each input value by expression result. |

The package does not choose an expression language. Applications provide one or
more `IFlowExpressionEngine` implementations during registration.

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterControlComponents(options => options
        .UseExpressionEngine(appExpressionEngine)
        .RegisterType<AppMessage>("app.message")
        .UseContextFactory(new AppMessageContextFactory()));
```

Basic configuration:

```json
{
  "type": "flow.when",
  "inputType": "object",
  "engine": "my-engine",
  "expressionId": "route-v1",
  "expressionName": "route-important",
  "expression": "..."
}
```

`inputType` defaults to `object`. Register type aliases when a control node
needs to connect to typed ports. Omit `engine` to use the default expression
engine configured by the host.

Expression evaluation failures emit `FlowError` and the node continues
processing later messages. Nodes emit diagnostics with input type, engine,
expression id, expression name, and route metadata where available.

## Design Metadata

This package exposes a package-owned `IComponentDesignMetadataProvider` for its
node types. Hosts can compose it through `ComponentDesignMetadataCatalog` to
populate palettes, editors, validation views, and documentation without
duplicating package descriptors.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
