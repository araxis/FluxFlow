# FluxFlow.Components.Assertions.Composition

Optional `FluxFlow.Composition` registration helpers and Designer metadata for
assertion components. The canonical `flow.assert` contract consumes `FlowValue`
and emits `FlowResult<FlowValueAssertionResult>` on one normal output.

The package does not choose an expression language, scan assemblies, resolve
CLR types from strings, or own expression-engine resources.

## Canonical Registration

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>(
    "Resources.Expressions.Primary",
    expressionEngine);

registry.RegisterAssertion();
```

| Type | Node | Input | Output |
|------|------|-------|--------|
| `flow.assert` | `FlowValueAssertionNode` | `FlowValue` | `FlowResult<FlowValueAssertionResult>` |

Passed and failed assertions are successful result kinds. Missing input and
expression evaluation failures are normal error results. The canonical
contract has no `Passed`, `Failed`, or universal error port.

## Flat Definition

```json
{
  "Resources": {
    "Expressions": {
      "Primary": {
        "Type": "host.expression"
      }
    },
    "Contexts": {
      "Score": {
        "Type": "host.assertion-context"
      }
    }
  },
  "Workflows": {
    "OrderChecks": {
      "CheckScore": {
        "Type": "flow.assert",
        "engine": "Resources.Expressions.Primary",
        "contextFactory": "Resources.Contexts.Score",
        "expression": "score >= 10",
        "expressionName": "minimum-score",
        "description": "score-check",
        "failureMessage": "Score too low.",
        "inputType": "order",
        "boundedCapacity": 128
      }
    }
  }
}
```

Component settings and resource references are flat. Hosts register the
referenced expression engine as a keyed `IFlowExpressionEngine` using the exact,
case-sensitive resource address.

`contextFactory` is an optional keyed `IFlowMapContextFactory<FlowValue>`
reference and `clock` is an optional keyed `TimeProvider` reference. All three
resource hints use `Resources.{name}`. The host owns registration, lifetime,
and disposal.

Invalid options, such as a missing expression or non-positive bounded capacity,
fail during node activation.

## Typed Compatibility Registration

Existing code-authored hosts can retain explicit CLR contracts:

```csharp
registry.RegisterAssertion<OrderMessage>("flow.assert.order");
```

That overload creates `FlowAssertionComponent<TInput>` and preserves its
`Input`, `Output`, `Passed`, `Failed`, Events, and Errors surfaces. The
`emitPassedInput` and `emitFailedInput` options apply only to this generic
compatibility registration. Use a distinct node type when canonical and generic
registrations share one registry.

## Design Metadata

`AssertionsComponentDesignMetadataProvider` describes the canonical node:

- `Input`: `FlowValue`
- `Output`: `FlowResult<FlowValueAssertionResult>`
- required `engine` resource, plus optional `contextFactory` and `clock`
- option section, importance, editor, syntax, and related-resource hints
- host-owned resource pickers using `Resources.{name}` key patterns

The metadata is descriptive only. Hosts own palette and inspector rendering,
resource selection, validation UI, activation, and persistence.
