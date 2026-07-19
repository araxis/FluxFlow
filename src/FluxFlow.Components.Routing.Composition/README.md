# FluxFlow.Components.Routing.Composition

Optional `FluxFlow.Composition` registration helpers and Designer metadata for
canonical routing components. Window, Correlation, and Join consume immutable
`FlowValue` messages and expose one normal `FlowResult<T>` output.

This package does not choose an expression language, scan assemblies, resolve
CLR types from strings, or own selector and clock resources.

## Canonical Registration

```csharp
services.AddKeyedSingleton<Func<FlowValue, string?>>(
    "Resources.Routing.Key",
    value => value.GetObject()["key"].GetString());
services.AddKeyedSingleton<Func<FlowValue, string?>>(
    "Resources.Routing.Side",
    value => value.GetObject()["side"].GetString());

registry
    .RegisterWindow()
    .RegisterCorrelation()
    .RegisterJoin();
```

| Type | Required resources | Input | Output |
|------|--------------------|-------|--------|
| `flow.window` | none | `Input: FlowValue` | `FlowResult<FlowWindow<FlowValue>>` |
| `flow.correlation` | `keySelector`, `sideSelector` | `Input: FlowValue` | `FlowResult<FlowCorrelationOutcome<FlowValue>>` |
| `flow.join` | `leftKeySelector`, `rightKeySelector` | `Left: FlowValue`, `Right: FlowValue` | `FlowResult<FlowJoinOutcome<FlowValue,FlowValue>>` |

`clock` is an optional keyed `TimeProvider` resource. Expected selector,
validation, and capacity failures remain normal error results on `Output`; the
canonical descriptors do not expose a universal Errors port.

## Flat Definition

```json
{
  "Resources": {
    "Routing": {
      "Key": {
        "Type": "host.selector"
      },
      "Side": {
        "Type": "host.selector"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Correlate": {
        "Type": "flow.correlation",
        "keySelector": "Resources.Routing.Key",
        "sideSelector": "Resources.Routing.Side",
        "requestSide": "request",
        "responseSide": "response",
        "timeoutMilliseconds": 30000,
        "maxPending": 1024,
        "boundedCapacity": 128,
        "Input": "Normalize.Output"
      }
    }
  }
}
```

Component settings, resource references, and port links are flat. Resource
addresses and component/port names are exact and case-sensitive. A link can be
a string, an array, or an object with `Port` and optional `Condition` fields.

Hosts register selector delegates and clocks as keyed services using the exact
resource address. Invalid options or missing required resources fail node
activation with composition diagnostics.

## Structural Routing Deprecation

`RegisterSwitch<TInput>()`, `RegisterFork<TInput>()`, and
`RegisterMerge<TInput>()` are obsolete. Canonical conditional links, output
fan-out, and shared-input fan-in replace these structural nodes. Their factories
and metadata remain available for compatibility, and Designer metadata marks
them deprecated with migration guidance.

## Typed Compatibility

Explicit generic overloads preserve the released typed contracts:

```csharp
registry
    .RegisterWindow<OrderMessage>("flow.window.order")
    .RegisterCorrelation<OrderMessage>("flow.correlation.order")
    .RegisterJoin<RequestMessage, ResponseMessage>("flow.join.requests");
```

Use distinct node type names when canonical and typed registrations share a
registry. Generic registrations preserve their direct match, timeout, and
Errors surfaces. Links never implicitly unwrap `FlowResult<T>` or convert
between `FlowValue` and arbitrary CLR types.

## Design Metadata

`RoutingComponentDesignMetadataProvider` describes:

- canonical FlowValue/result ports for Window, Correlation, and Join
- deprecated structural routing nodes and their existing dynamic-port metadata
- option section, importance, editor, syntax, and related-resource hints
- host-owned selector and clock pickers using exact `Resources.{name}` patterns

The metadata is descriptive only. Hosts own palette and inspector rendering,
resource selection, validation UI, activation, and persistence.
