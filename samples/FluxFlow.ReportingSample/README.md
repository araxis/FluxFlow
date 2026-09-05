# Consumer-owned reporting example

This non-packaged sample builds reports on FluxFlowApplication.Events without making reports part of the runtime library.

It demonstrates native Dataflow filtering, measurements across two workflow revisions, DI-selected report implementations, portable saved filters, idempotent file persistence and frozen historical queries. QuantityReport depends on the narrow IReportArchive read contract, not the concrete file archive. Add a report implementation and register it through DI; Engine needs no changes.

Run:

```shell
dotnet run --project samples/FluxFlow.ReportingSample/FluxFlow.ReportingSample.csproj
```

An optional directory argument retains the archive at a chosen location. Otherwise a fresh temporary directory is used and its path printed. Existing records are never overwritten.

The example emits quantities 10, 20 and 30, reopens the archive and applies a saved quantity >= 15 filter. Expected report count: 2. It waits for each measurement's capture acknowledgement to make the demonstration deterministic; that is not a production delivery guarantee.

## Ownership and completeness

ApplicationReportEvent, its cursor, report descriptors/catalog, portable filter language and IApplicationReport belong to this consumer sample. They are not Engine contracts. The archive wrapper stores consumer capture metadata around the unchanged FlowMessage.

A capture sequence counts received observations, not all runtime emissions. SourceCompletenessKnown remains false for live broadcast. The report is therefore marked Incomplete even when every received record is stored. An archive cannot recover events it never received.

The archive is single-process and file-per-record, with a one-MiB record limit and bounded query grouping. It is a demonstration, not a high-volume storage recommendation. Storage selection, retention, schema evolution, authentication and dashboard design are consumer decisions.
