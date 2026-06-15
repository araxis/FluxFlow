# FluxFlow.Components.Projections

Reusable event projection components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `event.projection` | `Input` -> `Output`, `Errors` | Projects matching runtime events into count, latest-event, and rolling-rate snapshots. |

`event.projection` consumes `FlowEvent` values from `FluxFlow.Engine` and emits
`EventProjectionSnapshot` values.

## Example

```json
{
  "type": "event.projection",
  "name": "failed-operations",
  "rateWindowSeconds": 60,
  "maxPreviewChars": 256,
  "filter": {
    "typePrefix": "operation.",
    "status": "failed",
    "subjectPrefix": "orders/",
    "attributes": {
      "tenant": "north"
    }
  }
}
```

The snapshot includes:

- observed event count
- matched event count
- rolling current rate
- first and last matched timestamps
- latest matching event summary
- latest payload preview with a configured length limit

## Filters

`EventFilter` supports:

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

Use `ProjectionsComponentOptions.UseClock(...)` for deterministic snapshot
timestamps in tests or hosts.

```csharp
registry.RegisterProjectionsComponents(options =>
    options.UseClock(timeProvider));
```

Event rate uses matching event timestamps. Snapshot timestamps use the
configured projection clock.

## Boundaries

This package has no UI dependency and no host-specific resource assumptions.
Hosts decide how snapshots are displayed, stored, tested, or forwarded.
