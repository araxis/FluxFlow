# FluxFlow.Components.Assertions

Reusable expression-driven assertion components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `flow.assert` | `Input` -> `Result`, `Passed`, `Failed`, `Errors` | Evaluates an expression and emits assertion results plus routed input values. |

The package does not choose an expression language. Applications provide one or
more `IFlowExpressionEngine` implementations during registration.

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterAssertionsComponents(options => options
        .UseExpressionEngine(appExpressionEngine)
        .RegisterType<AppMessage>("app.message")
        .UseContextFactory(new AppMessageContextFactory()));
```

Basic configuration:

```json
{
  "type": "flow.assert",
  "inputType": "object",
  "engine": "my-engine",
  "expressionId": "assert-v1",
  "expressionName": "valid-message",
  "description": "message is valid",
  "failureMessage": "Message failed validation.",
  "expression": "..."
}
```

`inputType` defaults to `object`. Register type aliases when an assertion node
needs to connect to typed ports. Omit `engine` to use the default expression
engine configured by the host.

False assertions emit a `FlowAssertionResult` and route the original input to
`Failed`. True assertions emit a result and route the original input to
`Passed`. Expression evaluation failures emit `FlowError` and the node
continues processing later messages.

## Design Metadata

This package exposes a package-owned `IComponentDesignMetadataProvider` for its
node types. Hosts can compose it through `ComponentDesignMetadataCatalog` to
populate palettes, editors, validation views, and documentation without
duplicating package descriptors.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
