# FluxFlow.Components.Journal

Reusable event journal contracts for FluxFlow hosts.

## What It Provides

- `JournalRecord` for normalized runtime event storage.
- `JournalEventInput` and `JournalRecordMapper` for mapping host event data into
  journal records without depending on a runtime package.
- `JournalEventInputBuilder` for fluent neutral event authoring and direct
  mapping through the existing record mapper.
- `JournalQuery` and `JournalQueryMatcher` for type, status, source, subject, channel, attribute, time range, and severity matching.
- `IJournalStore` for host-owned persistence.
- `IJournalStoreFactory`, `JournalStoreContext`, and `JournalStoreLease` for explicit host-owned store opening and ownership.
- `JournalComponentOptions` for direct hosts that need to configure journal store factories and clocks.
- `InMemoryJournalStore` for local runtime use and focused verification.
- `InMemoryJournalStoreFactory` for named shared in-memory stores.
- Retention options for cutoff and maximum-record pruning.

## Example

```csharp
var store = new InMemoryJournalStore();

var record = JournalEventInputBuilder
    .Create(DateTimeOffset.Parse("2026-01-01T00:00:00Z"))
    .WithType("job.completed")
    .WithSource("worker")
    .WithSubject("job/42")
    .WithStatus("ok")
    .WithWorkflow("orders", "Orders")
    .WithNode("complete-job")
    .WithSummary("Job completed")
    .AddAttribute("tenant", "primary")
    .BuildRecord("evt-1");

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

## Store Factories

Hosts that need deferred store opening can use an explicit factory and lease:

```csharp
var factory = new InMemoryJournalStoreFactory(new JournalRetentionOptions
{
    MaxRecords = 1000
});

await using var lease = await factory.OpenAsync(new JournalStoreContext
{
    StoreName = "default",
    Clock = TimeProvider.System
});

await lease.Store.AppendAsync(record);
```

`JournalStoreContext.StoreName` is trimmed and blank values are treated as the
default store. `JournalStoreLease.Owned(...)` disposes stores when the lease is
disposed; `JournalStoreLease.Shared(...)` leaves lifetime with the host.

Direct host configuration can use `JournalComponentOptions`:

```csharp
var options = new JournalComponentOptions()
    .UseStoreFactory(new InMemoryJournalStoreFactory())
    .UseClock(TimeProvider.System);
```

Hosts using keyed DI can register host-owned direct stores or store factories
without adding composition behavior:

```csharp
services
    .AddFluxFlowJournalStore("journal", store)
    .AddFluxFlowJournalStoreFactory("journal-factory", factory);
```

The direct registration overloads reject null stores and store factories. The
provider overloads fail with clear diagnostics if they return null.

## Composition

This package does not expose standalone nodes or `FluxFlow.Composition`
factories. It is a support package for host-owned journal persistence; workflow
nodes that emit events remain in their owning component packages.

Journal is runtime-neutral. Hosts that use another runtime should adapt runtime
events into `JournalEventInput` before calling `JournalRecordMapper`.
`JournalEventInputBuilder` is an authoring helper over the same contracts. It
does not own runtime event collection, persistence, retention, or store
lifetime; it creates normalized `JournalEventInput` snapshots and can map them
to `JournalRecord` through `JournalRecordMapper`.

Journal contracts normalize incoming text so record ids, optional fields, query
filters, and attribute keys/values are trimmed before storage or matching. Blank
attribute values and duplicate attribute keys after trimming are rejected to keep
query matching deterministic.

Record, event, and query attribute maps are copied on assignment. Query result
record lists are copied on assignment. Later caller mutations do not change
already-created records, queries, event inputs, or query results.

`JournalQueryMatcher.Validate()` owns structural query validation. It rejects
negative offsets, non-positive limits, and time ranges where `From` is later
than `To`; `InMemoryJournalStore.QueryAsync()` uses the same validator before
matching records.

`JournalRetentionOptions` rejects negative `MaxRecords` values and non-positive
`MaxAge` values when assigned. `InMemoryJournalStore.PruneAsync()` still owns
cross-field retention validation, including requiring `ReferenceTime` when
`MaxAge` is configured.
