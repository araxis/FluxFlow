# FluxFlow.Components.Mapping

Reusable mapping components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `flow.mapper` | `Input` -> `Output` | Maps each input message with a host-provided expression engine. |

The package does not choose an expression language. Applications provide one or
more `IFlowExpressionEngine` implementations during registration.

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .RegisterMappingComponents(options => options
        .UseExpressionEngine(appExpressionEngine)
        .RegisterType<AppInput>("app.input")
        .RegisterType<AppOutput>("app.output")
        .UseContextFactory(new AppInputContextFactory()));
```

Basic configuration:

```json
{
  "type": "flow.mapper",
  "inputType": "object",
  "outputType": "object",
  "engine": "my-engine",
  "expressionId": "normalize-v1",
  "expressionName": "normalize-message",
  "expression": "..."
}
```

`inputType` and `outputType` default to `object`. Register type aliases when the
mapper needs to connect to typed ports. `targetType` is accepted as an alias for
`outputType`. Omit `engine` to use the default expression engine configured by
the host.

Mapping failures emit `FlowError` and the node continues processing later
messages. The node also emits diagnostics for successful and failed mappings
with input type, output type, engine name, expression id, and expression name
when supplied.
