# FluxFlow.Components.Expectations

Reusable event expectation components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `event.expect` | `Input` -> `Result`, `Errors` | Waits for a matching runtime event and emits one expectation result. |
| `event.guard` | `Input` -> `Result`, `Errors` | Guards against a matching runtime event and emits one guard result. |

Both nodes consume `FlowEvent` values from `FluxFlow.Engine` and emit
`EventExpectationResult` values.

## Example

```json
{
  "type": "event.expect",
  "name": "order-completed",
  "timeoutMilliseconds": 5000,
  "maxObservedEvents": 10,
  "maxPreviewChars": 256,
  "filter": {
    "type": "operation.completed",
    "status": "ok",
    "subjectPrefix": "orders/"
  }
}
```

`event.guard` uses the same options, but a matching event means the guard is not
satisfied.

## Result

`EventExpectationResult` includes:

- evaluated timestamp
- expectation name
- expectation kind
- satisfied flag
- matched flag
- timeout flag
- matched event summary when one exists
- recent observed event summaries
- filter copy
- reason text

## Filters

Expectations use the same neutral `EventFilter` contract as the Projections
package. Filters support:

- event type or type prefix
- subject prefix
- channel prefix
- excluded subject prefix
- excluded channel prefix
- status
- source
- source node id
- component id through an event attribute named `componentId`
- attribute key/value pairs
- event timestamp range

Filters use ordinal string comparison.

## Timing

Use `ExpectationsComponentOptions.UseClock(...)` for deterministic timeout
and result timestamps in tests or hosts.

```csharp
registry.RegisterExpectationsComponents(options =>
    options.UseClock(timeProvider));
```

## Boundaries

This package has no UI dependency, no concrete transport dependency, and no
host-specific scenario runner dependency. Hosts decide how to feed events into
the nodes and how to display, store, or assert the emitted results.
