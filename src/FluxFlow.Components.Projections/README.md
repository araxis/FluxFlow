# FluxFlow.Components.Projections

Standalone event projection nodes for FluxFlow. The canonical node retains the
typed event and snapshot domain contracts while representing successful
snapshots and expected failures through one normal `FlowResult<T>` output. No
Composition or Engine package is required.

## Canonical Node

| Node | Input | Output |
|------|-------|--------|
| `FlowEventProjectionNode` | `ProjectionEvent` | `FlowResult<EventProjectionSnapshot>` |

The node also exposes Events for lifecycle and projection diagnostics. It has
no universal Errors port.

```csharp
await using var node = new FlowEventProjectionNode(
    new EventProjectionOptions
    {
        Name = "failed-operations",
        RateWindowSeconds = 60,
        MaxPreviewChars = 256,
        Filter = new EventFilter
        {
            TypePrefix = "operation.",
            Status = "failed",
            SubjectPrefix = "orders/",
            Attributes = new Dictionary<string, string>
            {
                ["tenant"] = "north"
            }
        }
    });

node.Output.LinkTo(results);

await node.Input.SendAsync(FlowMessage.Create(new ProjectionEvent
{
    Timestamp = DateTimeOffset.UtcNow,
    Type = "operation.completed",
    Source = "orders",
    Subject = "orders/42",
    Status = "failed"
}));
```

Each matching event updates observed/matched counts, first and last match times,
the latest event summary, and the rolling event-time rate. The emitted snapshot
uses the injected `TimeProvider` for its timestamp. Payload previews are
truncated to `MaxPreviewChars`. Filtered events produce no result but remain in
the observed count reported by a later or final snapshot.

## Result Contract

Matching snapshots use the `snapshot` result kind. When
`EmitFinalSnapshot = true`, normal `Complete()` drains accepted input and emits
one `final-snapshot` result before completing Output. The final rolling rate is
evaluated against the last matched event timestamp, so replayed historical
streams retain meaningful rates.

Invalid event data and unexpected projection evaluation failures use the
`projection-failed` kind and `projection.failed` error code. Immutable error
details include event context, exception type, and the released numeric error
code. These are normal output values and later accepted events continue.

Snapshot and failure messages preserve correlation, trace, causation, and
headers through `FlowMessage<T>.With(...)`. A final snapshot keeps complete
lineage from the last matching event; when nothing matched it starts a new
exchange.

## Filters

`EventFilter` supports exact event type, type/subject/channel prefixes,
excluded subject/channel prefixes, status, source, source node, component id,
attribute pairs, and an event timestamp range. String comparison is ordinal.
A null filter is normalized to match all events.

## Lifecycle

`Complete()` drains accepted events and handles the configured final snapshot.
`CompleteWithFinalSnapshotAsync()` completes and waits for the same canonical
lifecycle. `Fault(exception)` remains the unexpected data-path fault surface,
and `DisposeAsync()` completes and drains the node.

## Direct-Result Compatibility

`EventProjectionNode` remains available with its released direct
`EventProjectionSnapshot` Output, Errors port, Events, and explicit
`CompleteWithFinalSnapshotAsync()` flush behavior. It is a compatibility surface
for existing code-authored workflows. No implicit conversion exists between its
output and `FlowResult<EventProjectionSnapshot>` links.

## Composition

Use `FluxFlow.Components.Projections.Composition` when a Composition host should
register the canonical `event.project` factory and Designer metadata. Hosts
own optional keyed clocks and decide how snapshots are stored, displayed, or
forwarded.
