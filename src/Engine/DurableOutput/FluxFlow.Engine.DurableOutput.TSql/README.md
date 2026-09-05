# FluxFlow.Engine.DurableOutput.TSql

Production opt-in T-SQL storage for FluxFlow durable-output capture, delivery
state with exact lease renewal, dead-letter inspection/replay, read-only operational status, and bounded
terminal retention. The
provider uses direct parameterized SQL and the existing durable-output
contracts; it does not add an ORM or background delivery worker.

```csharp
using FluxFlow.Engine.DurableOutput.TSql;

services.AddFluxFlowTSqlDurableOutput(options =>
{
    options.ConnectionString = configuration.GetConnectionString("FluxFlowDurableOutput");
    options.SchemaManagement = TSqlDurableOutputSchemaManagement.ValidateOnly;
});
```

Registration and service resolution do not connect to the database. The first store operation creates or validates schema according to `SchemaManagement`. Use `CreateOrMigrate` with a deployment identity permitted to create provider-owned objects, or `ValidateOnly` with a restricted runtime identity after deployment has prepared the schema.

The initial supported and tested target is SQL Server 2022 with locking read committed (`READ_COMMITTED_SNAPSHOT OFF`). Delivery is at-least-once, so downstream handlers and sinks must remain idempotent.

Long-running handlers use the core dispatcher's flat
`LeaseRenewalInterval` setting. Renewal changes only the current exact token's
unexpired lease-expiry columns through one parameterized compare-and-set update;
it adds no schema object, migration, worker, ORM, or provider setting.

The same singleton implements `IDurableOutputStatusStore`. Status reports
capture/delivery counts, unmaterialized records, readiness, and lease expiry in
one payload-free aggregate statement. It does not invoke schema management,
backfill delivery rows, or change persisted state.

`IDurableOutputRetentionStore` resolves to the same singleton. It selects only
old completed or dead-lettered delivery rows in one bounded locking transaction
and deletes their capture parents; the existing foreign-key cascade removes
delivery state atomically. No cleanup is scheduled automatically, and no new
schema object, ORM, or application option is introduced. Purging ends the
identity's idempotency or dead-letter replay window.
