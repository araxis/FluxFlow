# FluxFlow.Components.Routing

Reusable expression-driven routing components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `flow.switch` | `Input` -> `Result`, `Matched`, `Default`, `Errors` | Evaluates a route key expression and routes the original input by match status. |

The package does not choose an expression language. Applications provide one or
more `IFlowExpressionEngine` implementations during registration.

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterRoutingComponents(options => options
        .UseExpressionEngine(appExpressionEngine)
        .RegisterType<AppMessage>("app.message")
        .UseContextFactory(new AppMessageContextFactory()));
```

Basic configuration:

```json
{
  "type": "flow.switch",
  "inputType": "app.message",
  "engine": "my-engine",
  "expression": "category",
  "routes": [ "priority", "standard" ],
  "defaultRoute": "unknown"
}
```

`Result` emits `FlowSwitchResult<TInput>` so downstream links can inspect
`RouteKey`, `Matched`, and the original `Value`. `Matched` emits the original
input when the key is in `routes`. `Default` emits the original input when the
key is empty or not configured.

If `routes` is empty, every non-empty route key is treated as matched. This lets
hosts use the result envelope and link conditions without predeclaring every
route key.

Expression evaluation failures emit `FlowError` and the node continues
processing later messages.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
