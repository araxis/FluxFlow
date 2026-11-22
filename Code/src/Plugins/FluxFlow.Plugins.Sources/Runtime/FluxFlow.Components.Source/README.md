# FluxFlow.Components.Source

Source sends the next sample value whenever Input receives an ordinary trigger message.

```csharp
await using var source = new Source(new SourceOptions
{
    Values = [firstValue, secondValue, JsonFlowValue.Null]
});
source.Output.LinkTo(target);
await source.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
source.Complete(); // when no further triggers will arrive
```

Values may have different FlowValue types. The list is snapshotted at construction;
immutable payloads are shared without JSON conversion. Each trigger derives a new
message using WithValue: its trace/correlation/headers are retained and causation
points to the trigger. Input payload is otherwise ignored. Processing is serial
and advances one item per ordinary trigger. Error messages pass through unchanged
without consuming an item. Empty/exhausted lists produce no ordinary output and
do not wrap around. Completion is explicit or propagated from upstream input.

Source has no timer, Interval, StartAsync or StopProduction. A separate Scheduler can
provide trigger messages through normal links. Complete drains accepted work;
Fault aborts. Source constructs a native TransformManyBlock to consume triggers and
declares its blocks to FlowLifetime; it is not an independently started producer.
OutputCapacity defaults to 128. CLR null values are invalid; use JsonFlowValue.Null.
No generator delegate, transport, engine or settings base is involved. Application
composition owns port names and registration.
