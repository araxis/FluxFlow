# FluxFlow.Components.Routing.Composition

Optional `FluxFlow.Composition` registration helpers and Designer metadata for
canonical routing components. Window, Correlation, and Join consume immutable
`FlowValue` messages and expose one normal `FlowResult<T>` output.

This package does not choose an expression language, scan assemblies, resolve
CLR types from strings, or own selector and clock resources.

Existing definitions using `flow.correlation` remain supported as a hidden
alias; new definitions and Designer palettes use `flow.correlate`.

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
| `flow.correlate` | `keySelector`, `sideSelector` | `Input: FlowValue` | `FlowResult<FlowCorrelationOutcome<FlowValue>>` |
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
        "Type": "flow.correlate",
        "keySelector": "Resources.Routing.Key",
        "sideSelector": "Resources.Routing.Side",
        "requestSide": "request",
        "responseSide": "response",
        "timeoutMilliseconds": 30000,
        "maxPending": 1024,
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

## Structural Routing Migration

Version 3 removes `RegisterSwitch<TInput>()`, `RegisterFork<TInput>()`, and
`RegisterMerge<TInput>()` together with their Designer metadata. Canonical
conditional links, output fan-out, and shared-input fan-in replace these
structural registrations. Migrate definitions before upgrading. Use an
explicit mapper when a former route envelope must become part of the payload.

Version 3 also removes generic Window, Correlation, and Join registration
overloads. Register the fixed canonical factories and convert CLR payloads to
`FlowValue` at the application boundary. Match, timeout, and expected failure
outcomes all use the single normal `Output` port.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, and omits legacy `name`, `boundedCapacity`,
`maxDegreeOfParallelism`, and `ensureOrdered` options from normal editing.
Default execution requires no processing profile; raw provider metadata retains
released declarations for compatibility.


`RoutingComponentDesignMetadataProvider` describes:

- canonical FlowValue/result ports for Window, Correlation, and Join
- option section, importance, editor, syntax, and related-resource hints
- host-owned selector and clock pickers using exact `Resources.{name}` patterns

The metadata is descriptive only. Hosts own palette and inspector rendering,
resource selection, validation UI, activation, and persistence.
