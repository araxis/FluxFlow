# FluxFlow.Components.Mapping

A standalone mapper node for FluxFlow. It depends only on `FluxFlow.Nodes` and
`FluxFlow.Mapping` — no engine, registry, or runtime. You `new` the node and
`LinkTo` the next one.

## Node

| Node | Shape | Purpose |
|------|-------|---------|
| `FlowMapperNode<TInput, TOutput>` | `Input` -> `Output`, `Failed` | Maps each message with a host-provided expression engine. |

Every message travels as a `FlowMessage<T>` envelope. The mapped result is
broadcast on `Output` as `FlowMessage<TOutput>`, carrying the same correlation id
as the input. When a mapping throws, the original input is fanned to `Failed`
(as `FlowMessage<TInput>`, same correlation id) and a `FlowError` surfaces on
`Errors`; the node keeps processing later messages.

The package does not choose an expression language. Applications provide an
`IFlowExpressionEngine` (from `FluxFlow.Mapping`); the node compiles the mapping
expression once at construction and evaluates the compiled form per message.

```csharp
var options = new MapperOptions
{
    Expression = "...",
    ExpressionName = "normalize-message",
    InputType = "app.input",
    OutputType = "app.output"
};

await using var node = new FlowMapperNode<AppInput, AppOutput>(
    options,
    appExpressionEngine,
    contextFactory: new TypedMappingContextFactory<AppInput>(new AppInputContextFactory()));

node.Output.LinkTo(resultSink, new DataflowLinkOptions { PropagateCompletion = false });
node.Failed.LinkTo(deadLetterSink, new DataflowLinkOptions { PropagateCompletion = false });

await node.Input.SendAsync(FlowMessage.Create(appInput));
```

`TInput` and `TOutput` are the real CLR types the node maps between. `MapperOptions`
carries the descriptive metadata (`InputType`, `OutputType`/`targetType`,
`ExpressionId`, `ExpressionName`) used in diagnostics and error context, plus the
`Expression` itself and the input `BoundedCapacity`.

## Mapping context

By default the node exposes the message payload as the `input` and `value`
variables on the per-message `FlowMapContext`. Pass an `IMappingContextFactory`
(for example a `TypedMappingContextFactory<TInput>` wrapping an
`IFlowMapContextFactory<TInput>` from `FluxFlow.Mapping`) to control the variables
each expression evaluates against:

```csharp
var node = new FlowMapperNode<AppInput, AppOutput>(
    options,
    appExpressionEngine,
    contextFactory: new TypedMappingContextFactory<AppInput>(new AppInputContextFactory()));
```

## Behavior

Mapping failures emit a `FlowError` on `Errors` (carrying the input's correlation
id and `MappingErrorCodes.MapperFailed`), fan the original input to `Failed`, and
the node keeps processing later messages. A compiled-mapper path that returns a
wrong-typed or null value surfaces a clearer "incompatible or null value" message
naming the expected output type. Per-message `flow.mapper.succeeded` /
`flow.mapper.failed` events flow on the `Events` port with input type, output
type, engine name, and the expression id/name when supplied.

## Runtime timing

Diagnostics use the node's clock for `Timestamp` (default `TimeProvider.System`).
Provide a deterministic clock for tests:

```csharp
new FlowMapperNode<AppInput, AppOutput>(options, engine, clock: new FakeTimeProvider(timestamp));
```

## Composition

Add `FluxFlow.Components.Mapping.Composition` when a host wants to instantiate
mapper nodes from `FluxFlow.Composition` fluent/config definitions. That optional
package registers closed generic `FlowMapperNode<TInput,TOutput>` factories.

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>("default", expressionEngine);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry =>
        registry.RegisterMapper<AppInput, AppOutput>());
```

Use custom node type names when one host needs several mapper type pairs:

```csharp
registry.RegisterMapper<HttpResponseOutput, MqttPublishRequest>(
    "flow.mapper.http-to-mqtt");
```

Composition resolves the expression engine from the keyed `engine` resource.
Optional keyed `contextFactory` and `clock` resources can provide custom mapping
context variables and deterministic diagnostics. The configured `InputType`,
`OutputType`, and `targetType` remain diagnostic metadata; CLR port types come
from the closed generic registration.
