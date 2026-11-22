# FluxFlow.Components.ChangeFilter

ChangeFilter<TKey> forwards the original message when its selected key differs
from the last forwarded key. A, A, B, A becomes A, B, A. The first ordinary input
always passes. Suppressed inputs do not change the comparison baseline. Unlike
Reducer it creates no new state payload or envelope; message identity is preserved.

```csharp
await using var filter = new ChangeFilter<string>(new()
{
    KeySelector = message => message.Value.Json.GetProperty("status").GetString()!,
    AreEqual = StringComparer.OrdinalIgnoreCase.Equals
});
upstream.Output.LinkTo(filter.Input);
filter.Output.LinkTo(downstream.Input);
```

KeySelector receives the whole message so it can select payload fields, headers,
IDs or a CLR value/tuple. It never forces JSON conversion; that is the selector's
choice. Keys must be non-null stable snapshots: do not mutate a selected object
after returning it. AreEqual defaults to EqualityComparer<TKey>.Default.Equals.
It can instead implement threshold comparison, such as Math.Abs(a - b) < 0.5;
comparison is always against the last forwarded value, not the last received value.
CLR reference types without value equality use reference equality by default;
provide explicit comparison when that is not the intended meaning.

Incoming FlowError messages bypass unchanged and do not update the remembered key.
Selector/comparison exceptions and null keys become normal processing errors;
they do not replace the previous key. Execution is serial and ordered (parallel or
unordered options rejected). Native Execution options control input capacity,
scheduler and cancellation. OutputCapacity defaults to 128. Suppressed messages
produce no acknowledgment; putting a filter inside request/reply may leave callers
waiting if no downstream result is produced. State is local/in-memory, cleared on
disposal. Complete drains accepted input; faults/cancellation use Core lifecycle.

Only one key is remembered globally per instance; there is no implicit per-device
grouping. A separate instance or explicit composition is needed for that behavior.
No dependency beyond Core; separate package from Reducer and Deduplicate.
