# FluxFlow.Components.Assertions

Standalone expression-driven assertion nodes. The canonical node evaluates
immutable `FlowValue` messages and returns pass, fail, or expected evaluation
errors through one normal `FlowResult<T>` output. It does not require
`FluxFlow.Engine`, a registry, or a runtime host.

## Canonical Node

| Node | Input | Output | Diagnostics |
|------|-------|--------|-------------|
| `FlowValueAssertionNode` | `FlowValue` | `FlowResult<FlowValueAssertionResult>` | `Events` |

Passing and failing rules are both successful result variants. Expression
evaluation failures and invalid message input are normal error results, so a
workflow can inspect `Kind`, `IsError`, and `Error` without a universal error
port. Unexpected implementation faults remain observable through `Completion`.

```csharp
var options = new FlowValueAssertionOptions
{
    Expression = "score >= 10",
    InputType = "order",
    Description = "score-check",
    FailureMessage = "Score too low."
};

await using var node = new FlowValueAssertionNode(
    options,
    expressionEngine,
    contextFactory);

var results = new BufferBlock<
    FlowMessage<FlowResult<FlowValueAssertionResult>>>();
node.Output.LinkTo(results);

var input = FlowValue.FromObject(new Dictionary<string, FlowValue>
{
    ["score"] = FlowValue.From(12L)
});

await node.Input.SendAsync(FlowMessage.Create(input));
var result = (await results.ReceiveAsync()).Payload;
```

The expression is compiled once during construction. The package does not
choose an expression language; the host supplies an `IFlowExpressionEngine`
from `FluxFlow.Mapping`.

## Result Contract

`AssertionResultKinds.Passed` and `AssertionResultKinds.Failed` carry a
`FlowValueAssertionResult` with the exact input instance, decision, description,
message, expression metadata, engine name, semantic input type, and timestamp.
A failed rule has `IsError = false`.

`MissingInput` and `EvaluationFailed` have `IsError = true` and a data-owned
`FlowError`. Error details use immutable `FlowValue` data and do not expose raw
exceptions. Expression failures do not stop later messages.

Output envelopes preserve correlation, trace, and headers while identifying the
consumed message through causation. Events use the same correlation id and the
injected `TimeProvider`.

`FlowResult<FlowValueAssertionResult>` is a real typed payload. It is not
implicitly unwrapped into `FlowValueAssertionResult`; downstream extraction or
routing requires an explicit result-aware component or mapper.

## Expression Context

By default, the expression engine receives the exact `FlowValue` as both
`input` and `value`. Supply an `IFlowMapContextFactory<FlowValue>` when an
expression needs additional data-shaped variables. Do not place clients,
mutable services, or secrets in expression contexts.

`FlowValueAssertionOptions` validates construction-time invariants: expression
is required, `InputType` cannot be empty, and `BoundedCapacity` must be positive.

## Typed Compatibility

`FlowAssertionComponent<TInput>`, `AssertionOptions`, `FlowAssertionResult`,
`Passed`, `Failed`, and `Errors` remain available for existing code-authored
workflows. That generic node preserves optional routed-input behavior and its
numeric `AssertionErrorCodes` surface.

```csharp
await using var node = new FlowAssertionComponent<AppMessage>(
    new AssertionOptions
    {
        Expression = "score >= 10",
        EmitPassedInput = true,
        EmitFailedInput = true
    },
    expressionEngine,
    appMessageContextFactory);
```

New configuration-authored workflows should use `FlowValueAssertionNode` so
rule outcomes and expected failures remain normal workflow data.

## Composition

Add `FluxFlow.Components.Assertions.Composition` for canonical configuration or
fluent registration:

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>(
    "Resources.Expressions.Primary",
    expressionEngine);

registry.RegisterAssertion();
```

Parameterless registration owns canonical `data.assert`. Explicit
`RegisterAssertion<TInput>(customNodeType)` remains available for typed
compatibility and should use a distinct node type when both forms share a
registry.
