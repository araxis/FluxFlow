# FluxFlow.Components.Assertions

A standalone expression-driven assertion node for FluxFlow. It depends only on
`FluxFlow.Nodes` and `FluxFlow.Mapping` — no engine, registry, or runtime. You
`new` the node and `LinkTo` the next one.

## Node

| Node | Shape | Purpose |
|------|-------|---------|
| `FlowAssertionComponent<TInput>` | `Input` -> `Output` (result), `Passed`, `Failed` | Evaluates an input against a boolean expression. |

Every message travels as a `FlowMessage<T>` envelope. The assertion result is
broadcast on `Output` as `FlowMessage<FlowAssertionResult>`; the original input
is fanned to `Passed` when the assertion holds or `Failed` when it does not (each
`FlowMessage<TInput>`). All three carry the same correlation id as the input.

The package does not choose an expression language: applications supply an
`IFlowExpressionEngine` (from `FluxFlow.Mapping`). The node compiles the boolean
expression once at construction, so each message only evaluates the compiled
form.

```csharp
var node = new FlowAssertionComponent<AppMessage>(
    new AssertionOptions
    {
        Expression = "score >= 10",
        InputType = "app.message",
        Description = "score-check",
        FailureMessage = "Score too low."
    },
    appExpressionEngine,
    contextFactory: new AppMessageContextFactory());

node.Output.LinkTo(resultSink, new DataflowLinkOptions { PropagateCompletion = false });
node.Passed.LinkTo(passedSink, new DataflowLinkOptions { PropagateCompletion = false });
node.Failed.LinkTo(failedSink, new DataflowLinkOptions { PropagateCompletion = false });

await node.Input.SendAsync(FlowMessage.Create(new AppMessage(score: 12)));
```

`AssertionOptions` validates at construction: a missing `Expression`, an empty
`InputType`, or a non-positive `BoundedCapacity` fails fast as an argument
exception.

## Mapping context

By default the node exposes the input as the `input` and `value` variables to the
expression engine. Pass an `IFlowMapContextFactory<TInput>` (from
`FluxFlow.Mapping`) to project named variables from the payload — for example a
`score` variable read by a `score >= 10` expression.

## Behavior

A failing assertion is not an error: the node emits a `FlowAssertionResult` with
`Status = Failed` and routes the original input to `Failed`. A passing assertion
emits a result and routes the input to `Passed`. Routing can be suppressed per
port via `AssertionOptions.EmitPassedInput` / `EmitFailedInput`. Expression
evaluation failures emit a `FlowError` on `Errors` (carrying the input's
correlation id and `AssertionErrorCodes.ExpressionFailed`) and the node keeps
processing later messages. Per-message `flow.assert.evaluated` /
`flow.assert.failed` events flow on the `Events` port.

## Runtime timing

Assertion results use the node's clock for `EvaluatedAt` (default
`TimeProvider.System`). Provide a deterministic clock for tests:

```csharp
new FlowAssertionComponent<object>(options, engine, clock: new FakeTimeProvider(timestamp));
```

## Composition

Add `FluxFlow.Components.Assertions.Composition` when a host wants to instantiate
assertion nodes from `FluxFlow.Composition` fluent/config definitions. That
optional package registers closed generic `FlowAssertionComponent<TInput>`
factories.

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>("default", expressionEngine);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry =>
        registry.RegisterAssertion<AppMessage>());
```

Use custom node type names when one host needs several input shapes:

```csharp
registry
    .RegisterAssertion<OrderMessage>("flow.assert.order")
    .RegisterAssertion<HttpResponseOutput>("flow.assert.http-response");
```

Composition resolves the expression engine from the keyed `engine` resource.
Optional keyed `contextFactory` and `clock` resources can provide custom
expression variables and deterministic result/diagnostic timestamps. The
configured `InputType` remains diagnostic metadata; CLR port types come from the
closed generic registration.
