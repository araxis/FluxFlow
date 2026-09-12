# Keyed exchange coordination

Transport-neutral coordination for operations that emit work and later receive
an acknowledgement, reply, cancellation, timeout, or fault. This Core implementation owns
pending keys, contexts, deadlines, terminal outcomes, and bounded settled-key
history. It does not own transports, broker acknowledgements, Dataflow blocks,
component ports, workflow configuration, or host lifetime.

`PendingExchangeCoordinator<TKey, TContext, TOutcome>` accepts any non-null key.
A trace identifies processing lineage, not a unique exchange: concurrent requests
may share it. Callers select an exchange or protocol key. The application-level
`FlowRequestReply` adapter combines a fresh correlation with the expected reply
address; this transport-neutral coordinator does not interpret either field.

The coordinator enforces a maximum in-flight count, rejects duplicate keys,
uses one `TimeProvider` timer with a deadline queue, and settles every accepted
exchange exactly once. A bounded history classifies feedback after success as
duplicate and feedback after timeout, cancellation, stop, or fault as late.
Callers that permit retries should include an attempt discriminator in `TKey`
so feedback from an older attempt cannot settle a newer one.

Finite timeouts must be positive. An explicit `Timeout.InfiniteTimeSpan` has no
scheduled deadline; it still occupies capacity and can be cancelled, resolved,
faulted or stopped. The default timeout remains finite. Deadline checks at
settlement prevent late replies from winning when a timer callback is delayed.

```csharp
await using var coordinator = new PendingExchangeCoordinator<Guid, DeliveryContext, DeliveryOutcome>();
var exchange = Guid.NewGuid();
var started = coordinator.TryStart(exchange, context);

if (started.IsAccepted)
{
    coordinator.TryResolve(exchange, DeliveryOutcome.Acknowledged);
    var completed = await started.Completion!;
}
```

This implementation is shipped inside Core, not as a separate coordination
package. Protocol-specific acknowledgement aggregation, response mapping,
connection lifetime and retry policy remain in their owning optional packages.
