# FluxFlow.Components.Routing

Reusable expression-driven routing components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `flow.switch` | `Input` -> `Result`, `Matched`, optional route outputs, `Default`, `Errors` | Evaluates a route key expression and routes the original input by match status. |
| `flow.correlation` | `Input` -> `Matched`, `Timeouts`, `Errors` | Pairs request and response style messages by key and side expressions. |
| `flow.window` | `Input` -> `Output`, `Errors` | Groups input items into count or time based windows. |

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
  "routeOutputs": {
    "priority": "Priority",
    "standard": "Standard"
  },
  "defaultRoute": "unknown"
}
```

`Result` emits `FlowSwitchResult<TInput>` so downstream links can inspect
`RouteKey`, `Matched`, and the original `Value`. `Matched` emits the original
input when the key is in `routes`. `Default` emits the original input when the
key is empty or not configured.

`routeOutputs` is optional. When configured, the runtime adds those output
ports and emits the original input to the matching route port. Several route
keys can map to the same output port. Route output port names must be valid
engine port names and cannot collide with built-in switch ports.

If `routes` is empty, every non-empty route key is treated as matched. This lets
hosts use the result envelope and link conditions without predeclaring every
route key.

Expression evaluation failures emit `FlowError` and the node continues
processing later messages.

## Correlation

Use `flow.correlation` when a workflow receives related messages on one stream
and needs to pair them by key.

```json
{
  "type": "flow.correlation",
  "inputType": "app.message",
  "engine": "my-engine",
  "keyExpression": "correlationId",
  "sideExpression": "kind",
  "requestSide": "request",
  "responseSide": "response",
  "timeoutMilliseconds": 30000,
  "maxPending": 1024
}
```

`Matched` emits `FlowCorrelationMatch<TInput>` with the request, response, key,
timestamps, and elapsed time. `Timeouts` emits
`FlowCorrelationTimeout<TInput>` for unmatched pending inputs when the timeout
is observed before the next input, and for any remaining pending inputs when
the node completes.

Invalid keys, unsupported sides, duplicate same-side messages, expression
failures, and pending-capacity failures emit `FlowError` and the node continues
processing later messages.

## Window

Use `flow.window` when a workflow needs batches of items before a downstream
node runs.

```json
{
  "type": "flow.window",
  "inputType": "app.message",
  "maxItems": 100,
  "timeMilliseconds": 5000,
  "emitPartialOnCompletion": true
}
```

`Output` emits `FlowWindow<TInput>` with a sequence number, item list, start and
emit timestamps, duration, count, and emit reason. `maxItems` emits when the
window reaches the configured count. `timeMilliseconds` emits when the current
window has been open for that duration, even if no later input arrives. When
both are configured, whichever condition happens first emits the window.

At least one boundary must be configured. On completion, a partial window is
emitted by default. Set `emitPartialOnCompletion` to `false` to discard partial
windows.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
