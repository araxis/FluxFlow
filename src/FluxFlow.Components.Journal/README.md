# FluxFlow.Components.Journal

Reusable event journal contracts for FluxFlow hosts.

## What It Provides

- `JournalRecord` for normalized runtime event storage.
- `JournalEventInput` and `JournalRecordMapper` for mapping host event data into
  journal records without depending on a runtime package.
- `JournalQuery` and `JournalQueryMatcher` for type, status, source, subject, channel, attribute, time range, and severity matching.
- `IJournalStore` for host-owned persistence.
- `InMemoryJournalStore` for local runtime use and focused verification.
- Retention options for cutoff and maximum-record pruning.

## Example

```csharp
var store = new InMemoryJournalStore();

var record = JournalRecordMapper.FromEvent(new JournalEventInput
{
    Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
    Type = "job.completed",
    Source = "worker",
    Subject = "job/42",
    Status = "ok",
    Attributes = new Dictionary<string, string>
    {
        ["tenant"] = "primary"
    }
}, "evt-1");

await store.AppendAsync(record);

var result = await store.QueryAsync(new JournalQuery
{
    TypePrefix = "job.",
    Attributes = new Dictionary<string, string>
    {
        ["tenant"] = "primary"
    },
    Limit = 10
});
```

## Composition

This package does not expose standalone nodes or `FluxFlow.Composition`
factories. It is a support package for host-owned journal persistence; workflow
nodes that emit events remain in their owning component packages.

Journal is runtime-neutral. Hosts that use another runtime should adapt runtime
events into `JournalEventInput` before calling `JournalRecordMapper`.

Journal contracts normalize incoming text so record ids, optional fields, query
filters, and attribute keys/values are trimmed before storage or matching. Blank
attribute values and duplicate attribute keys after trimming are rejected to keep
query matching deterministic.

Record, event, and query attribute maps are copied on assignment. Query result
record lists are copied on assignment. Later caller mutations do not change
already-created records, queries, event inputs, or query results.
