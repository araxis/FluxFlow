# FluxFlow.Coordination

Transport-neutral coordination for operations that emit work and later receive
an acknowledgement, reply, cancellation, timeout, or fault. The package owns
pending keys, contexts, deadlines, terminal outcomes, and bounded settled-key
history. It does not own transports, broker acknowledgements, Dataflow blocks,
component ports, workflow configuration, or host lifetime.

`PendingExchangeCoordinator<TKey, TContext, TOutcome>` accepts any non-null key.
Workflow components normally use `TraceId`, which identifies one end-to-end
workflow processing lineage. Adapters may select another protocol key when an
external contract requires it. `CorrelationId` is therefore interoperability
metadata rather than a dependency of this package.

The coordinator enforces a maximum in-flight count, rejects duplicate keys,
uses one `TimeProvider` timer with a deadline queue, and settles every accepted
exchange exactly once. A bounded history classifies feedback after success as
duplicate and feedback after timeout, cancellation, stop, or fault as late.
Callers that permit retries should include an attempt discriminator in `TKey`
so feedback from an older attempt cannot settle a newer one.

```csharp
var coordinator = new PendingExchangeCoordinator<TraceId, DeliveryContext, DeliveryOutcome>();
var started = coordinator.TryStart(message.TraceId, context);

if (started.IsAccepted)
{
    coordinator.TryResolve(message.TraceId, DeliveryOutcome.Acknowledged);
    var completed = await started.Completion!;
}
```

The package depends only on `FluxFlow.Nodes` and the BCL. Broker-specific
acknowledgement aggregation, HTTP response mapping, MQTT lifecycle, and retry
policy remain in their owning packages.
