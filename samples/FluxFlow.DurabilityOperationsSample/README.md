# FluxFlow durability operations sample

This non-server sample runs one durable input through a workflow, captures the
workflow output durably, and delivers it through a host-owned handler. It also
shows the two deliberately separate operational views:

- event metrics and activities are consumed by host-owned BCL listeners; and
- persisted status is read explicitly from the provider-neutral status stores.

Run it from the repository root:

```powershell
dotnet run --project samples/FluxFlow.DurabilityOperationsSample/FluxFlow.DurabilityOperationsSample.csproj
```

The sample uses separate SQL-file databases in a unique temporary directory so
it needs no server or credentials. The host and listeners are stopped and
disposed before that exact directory is deleted. SQL-file storage is only the
self-contained sample provider: the durable input/output and status contracts
remain provider-neutral and can be backed by the existing T-SQL providers or a
future provider.

## Operational ownership

FluxFlow emits semantic signals through the BCL sources
`FluxFlow.Engine.DurableInput` and `FluxFlow.Engine.DurableOutput`. The sample
attaches `MeterListener` and `ActivityListener` directly. A production host can
instead connect those same sources to its chosen OpenTelemetry-compatible
bridge and exporter; FluxFlow does not select exporter, sampling, retention, or
redaction policy.

Listener callbacks collect only bounded semantic outcomes and static operation
names. They do not print payloads, application addresses, message/trace/lease
identity, exception details, database paths, or connection data. Avoid adding
high-cardinality identity fields to metrics in a real host.

Status snapshots are on-demand persisted queries. This sample asks twice for
input status (before startup and after delivery) and once for output status
(after delivery). It installs no timer, health check, gauge callback, or
background database poller. A snapshot describes stored state at its explicit
observation time; it is not by itself a process-liveness guarantee.

Durable input and output remain at-least-once. The sample handler demonstrates
deduplication by the durable output key in memory; a real destination should
persist or otherwise enforce idempotency using that key when duplicates matter.
