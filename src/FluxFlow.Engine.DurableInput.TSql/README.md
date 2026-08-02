# FluxFlow.Engine.DurableInput.TSql

Opt-in networked T-SQL persistence for FluxFlow durable application inputs.
The provider implements durable-input store, dead-letter, exact lease-renewal,
read-only status, and bounded retention contracts with shared multi-process
leasing and at-least-once delivery semantics.

```csharp
services.AddFluxFlowTSqlDurableInput(options =>
{
    options.ConnectionString =
        configuration.GetConnectionString("FluxFlowDurableInput");
    options.SchemaManagement =
        TSqlDurableInputSchemaManagement.CreateOrMigrate;
});

services.AddFluxFlowDurableInput();
```

Use `CreateOrMigrate` when this application identity owns first-time schema
creation. Use `ValidateOnly` when deployment tooling owns schema changes. The
configured database must use locking read committed; snapshot-based read
committed is rejected because the provider uses `READPAST` for cooperative
multi-host leasing.

Registration and service resolution perform no database work. Connections are
opened from the standard pool per operation. FluxFlow does not retry ambiguous
state-changing commands or commits; hosts own credentials, access policy,
backups, monitoring, capacity, and retention.

This package does not alter `FluxFlowApplicationOptions`, create a background
worker, persist internal workflow checkpoints, or claim exactly-once delivery.

`IDurableInputStatusStore` resolves to the same singleton. It returns a
payload-free aggregate snapshot at an explicit observation time, using the
configured connection-open and command bounds without creating, migrating,
repairing, leasing, settling, or replaying data.

`IDurableInputRetentionStore` also resolves to the same singleton. It uses one
bounded locking-read-committed transaction to permanently delete old delivered
tombstones or dead letters, optionally scoped to one application address. No
cleanup runs automatically. Deleting a delivered identity ends its
deduplication window; deleting a dead letter removes its replay source. The
operation uses the existing schema and direct parameterized SQL.
