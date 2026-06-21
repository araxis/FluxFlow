# FluxFlow.Components.Journal

Reusable event journal contracts for FluxFlow hosts.

## What It Provides

- `JournalRecord` for normalized runtime event storage.
- `JournalQuery` and `JournalQueryMatcher` for type, status, source, subject, channel, attribute, time range, and severity matching.
- `IJournalStore` for host-owned persistence.
- `InMemoryJournalStore` for local runtime use and focused verification.
- Retention options for cutoff and maximum-record pruning.

## Example

```csharp
var store = new InMemoryJournalStore();

await store.AppendAsync(new JournalRecord
{
    Id = "evt-1",
    Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
    Type = "job.completed",
    Source = "worker",
    Subject = "job/42",
    Status = "ok",
    Attributes = new Dictionary<string, string>
    {
        ["tenant"] = "primary"
    }
});

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
