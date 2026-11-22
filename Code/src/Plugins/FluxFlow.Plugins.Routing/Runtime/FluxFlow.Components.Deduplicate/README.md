# FluxFlow.Components.Deduplicate

Deduplicate<TKey> forwards an ordinary message only when its selected key is not
currently remembered. A, A, B, A becomes A, B within the retention/capacity window.
The original message is forwarded unchanged, preserving payload and envelope identity.

```csharp
await using var filter = new Deduplicate<string>(new()
{
    KeySelector = message => message.Value.Json.GetProperty("eventId").GetString()!,
    Comparer = StringComparer.OrdinalIgnoreCase,
    Retention = TimeSpan.FromMinutes(5),
    MaxKeys = 10_000
});
upstream.Output.LinkTo(filter.Input);
filter.Output.LinkTo(downstream.Input);
```

Key means the selector result, not a special framework ID. Select any non-null
stable CLR key, including tuples; selector receives the whole message. Comparer
is a native IEqualityComparer<TKey> (default EqualityComparer<TKey>.Default).
It must provide stable, consistent equality and hash codes. No implicit JSON
serialization or deep comparison. Use immutable snapshots for object keys; a
mutable key or inconsistent/throwing comparer cannot provide reliable deduplication.

Retention is required and positive. It starts when an ordinary key first passes;
duplicates do not extend expiry or move it to the back. At the exact expiry boundary
the same key may pass again. Monotonic TimeProvider timestamps measure retention.
Expired entries are removed lazily when another ordinary input arrives, not by a
background timer. Idle memory remains bounded by MaxKeys (default 1024). When full,
the oldest remembered key is evicted to accept a new one; an evicted key can pass
again before Retention elapsed. This bounds the number of keys, not their byte size.

There is no persistence, cross-instance history or exactly-once delivery guarantee.
Remembering means passing this filter, not successful downstream processing.
Restart/eviction/expiry permit duplicates again. Suppression emits no acknowledgment;
using this inside request/reply needs an explicit scenario for suppressed requests.

Incoming FlowError messages bypass without selecting a key. Selection failures,
null keys and comparer exceptions use Core processing errors. Selector failures
leave history untouched; expiry/capacity cleanup is not a transactional store.
Execution must remain serial/ordered. Native execution settings retain input
capacity/cancellation support; output capacity defaults to 128. Complete drains
accepted work, and disposal clears retained keys. No application registration here.
Independent package depending only on Core.
