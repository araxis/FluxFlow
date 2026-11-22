# FluxFlow.Components.Batch

Batch collects ordinary messages and publishes one FlowMessage carrying a FlowBatch
when BatchSize is reached or MaxWait elapses from the first collected message.
Subsequent messages do not reset the timer. Empty batches are never emitted.

```csharp
await using var batch = new Batch(new BatchOptions
{
    BatchSize = 100,
    MaxWait = TimeSpan.FromSeconds(5)
});
source.Output.LinkTo(batch.Input, new() { PropagateCompletion = true });
batch.Output.LinkTo(next.Input);
```

FlowBatch.Items is a read-only snapshot of the original FlowMessage references,
including values, IDs, correlation and headers. Payloads remain immutable and
shared. No JSON conversion occurs during collection. Its lazy JSON representation
is an array of complete message envelopes, not just their values. The outer batch
message has its own identity/trace and no member's correlation or causation is
selected as the identity of the whole group. One reply to a batch does not resolve
all its members' requests. There is no automatic distributed-span link collection.

Complete drains accepted input and publishes the remaining partial batch. Fault
or cancellation aborts, discarding retained unpublished work and stopping timers.
FlowError inputs bypass unchanged without entering the batch or changing its timer;
an error can overtake ordinary messages still being collected.

BatchSize and MaxWait must be positive; MaxWait is limited to the platform delay
range (uint.MaxValue - 1 milliseconds). TimeProvider defaults to System and can be
supplied for deterministic tests. Execution accepts native Dataflow settings but
requires MaxDegreeOfParallelism = 1 and EnsureOrdered = true. No global clock loop
runs while the batch is empty. At most one current collection and its timer exist.

Collection size is bounded by BatchSize; input defaults to capacity 128 and output
to OutputCapacity 128 (measured in output messages/batches). Publication awaits
output admission and blocks further collection while output is full. MaxWait ends
the collection window, not a guarantee of downstream delivery by that time.
It starts when the first item is processed, not while that item waits in Input.
Timer/size/completion serialize access so each message belongs to exactly one batch
and normal batches remain ordered. Timer publication failures fault the lifecycle.
Output is a native BroadcastBlock: a late subscriber can receive its latest retained
batch, while intermediate batches can be missed by slow or declining consumers.
Connect consumers before sending inputs when they must observe each live publication.

Batch constructs its own input, Output and Events, and declares them to FlowLifetime.
The coordinator invokes the final flush after input drains. The local gate and timer
remain necessary to coordinate size-triggered, timed and final publication.

No grouping keys, join, processing/retry of members or application registration.
Independent project/package; depends only on Core.
