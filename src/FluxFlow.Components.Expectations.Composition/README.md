# FluxFlow.Components.Expectations.Composition

Optional `FluxFlow.Composition` registration helpers and Designer metadata for
projection-event expectations. The canonical `event.expect` contract
consumes `ProjectionEvent` and emits one
`FlowResult<EventExpectationResult>` output.

Existing definitions using `event.expectation` remain supported as a hidden
alias; new definitions and Designer palettes use `event.expect`.

## Canonical Registration

```csharp
services.AddKeyedSingleton<TimeProvider>(
    "Resources.Clocks.Workflow",
    clock);

registry.RegisterEventExpectation();
```

| Type | Node | Input | Output |
|------|------|-------|--------|
| `event.expect` | `FlowEventExpectationNode` | `ProjectionEvent` | `FlowResult<EventExpectationResult>` |

Matched and unmet rules, timeout, and input completion are successful result
variants. Expected evaluation failures are normal error variants on the same
Output. The canonical descriptor exposes Events and has no universal Errors
port.

## Flat Definition

```json
{
  "Resources": {
    "Clocks": {
      "Workflow": {
        "Type": "host.clock"
      }
    }
  },
  "Workflows": {
    "OrderMonitoring": {
      "WaitForCompletion": {
        "Type": "event.expect",
        "clock": "Resources.Clocks.Workflow",
        "kind": "Expect",
        "filter": {
          "Type": "operation.completed",
          "Status": "ok",
          "SubjectPrefix": "orders/"
        },
        "timeoutMilliseconds": 5000,
        "maxObservedEvents": 10,
        "maxPreviewChars": 256
      }
    }
  }
}
```

Component settings and the optional clock reference are flat. Hosts register
the clock as a keyed `TimeProvider` using the exact, ordinal,
case-sensitive resource address. The host owns resource lifetime and disposal.
Invalid static options fail node activation.

## Compatibility Boundary

The existing `EventExpectationNode` remains in the runtime package for direct
code-authored use. The Composition `2.x` package intentionally registers only
the canonical fixed contract. Existing Composition consumers can remain on the
published `1.x` package while migrating definitions and typed links.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, and omits legacy `name`, `boundedCapacity`,
`maxDegreeOfParallelism`, and `ensureOrdered` options from normal editing.
Default execution requires no processing profile; raw provider metadata retains
released declarations for compatibility.


`ExpectationsComponentDesignMetadataProvider` describes only the canonical
fixed node:

- `Input`: `ProjectionEvent`
- `Output`: `FlowResult<EventExpectationResult>`
- option section, importance, and editor hints
- optional host-owned clock picker using the `Resources.{name}` key pattern

The metadata is descriptive. This package does not own rendering, resource
creation, persistence, runtime updates, or implicit result extraction.
