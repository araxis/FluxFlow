# T-SQL Durable Outputs

`FluxFlow.Engine.DurableOutput.TSql` is the production opt-in networked durable-output provider. It implements capture, renewable leased delivery state, dead-letter inspection, generation-protected replay, payload-free status, and bounded terminal retention behind the existing provider-neutral contracts. Installing it does not change Engine or the in-process default, and registration performs no database I/O.

The initial supported and tested target is SQL Server 2022 with locking read committed and `READ_COMMITTED_SNAPSHOT OFF`. The package targets .NET 8 and .NET 10 and uses direct parameterized commands through `Microsoft.Data.SqlClient` 7.0.2. It contains no ORM, repository layer, reflection, runtime scanning, or background delivery worker.

## Choose the provider

Use `FluxFlow.Engine.DurableOutput.SqlFile` when one host owns one local SQLite file and simple local operations are the priority. Use `FluxFlow.Engine.DurableOutput.TSql` when multiple host processes or replicas need to share the same durable-output database and coordinate capture, leases, settlement, dead letters, and replay through a networked server.

Both packages implement the same provider-neutral capability contracts. A host selects exactly one provider for those contracts; provider configuration is deliberately not part of `FluxFlowApplicationOptions`.

## Registration

```csharp
using FluxFlow.Engine.DurableOutput.TSql;

services.AddFluxFlowTSqlDurableOutput(options =>
{
    options.ConnectionString =
        configuration.GetConnectionString("FluxFlowDurableOutput");
    options.CommandTimeout = TimeSpan.FromSeconds(30);
    options.SchemaLockTimeout = TimeSpan.FromSeconds(30);
    options.ConnectRetryCount = 1;
    options.ConnectRetryInterval = TimeSpan.FromSeconds(1);
    options.SchemaManagement =
        TSqlDurableOutputSchemaManagement.ValidateOnly;
});
```

The callback is a temporary flat builder. Registration resolves it into an immutable `TSqlDurableOutputStoreOptions` record, validates it before changing the service collection, and registers one `TSqlDurableOutputStore` singleton. `IDurableOutputStore`, `IDurableOutputDeliveryStore`, `IDurableOutputDeadLetterStore`, `IDurableOutputStatusStore`, and `IDurableOutputRetentionStore` are aliases of that same instance.

Equivalent repeated registration is idempotent. Different settings, partial provider registration, or an existing owner of any durable-output store contract fail before the collection is changed.

Registration, provider construction, and store resolution do not open a connection. The first operation initializes or validates the schema.

## Immutable settings

| Setting | Default | Rules |
|---------|---------|-------|
| `ConnectionString` | required | Must contain a server and database. Source it from host configuration or a secret provider. |
| `CommandTimeout` | 30 seconds | Whole seconds from 1 second through 10 minutes. Applies to runtime commands. |
| `SchemaLockTimeout` | 30 seconds | Whole milliseconds from zero through 10 minutes. Zero means no wait. |
| `ConnectRetryCount` | 1 | Zero through five; applied through the SQL client connection string. |
| `ConnectRetryInterval` | 1 second | Whole seconds from 1 through 60; applied through the SQL client connection string. |
| `SchemaManagement` | `CreateOrMigrate` | Selects controlled creation/migration or read-only validation. |

The immutable options record intentionally remains a record. The mutable object exists only while the registration callback executes. Its connection string is redacted from the options record's `ToString()` output.

## Schema deployment modes

`CreateOrMigrate` acquires the provider's transaction-owned application lock, creates a completely absent version-1 schema through the explicit migration pipeline, validates the complete shape, and commits as one transaction. A partial or unversioned schema is never repaired automatically.

`ValidateOnly` acquires the same bounded lock and validates the existing version and shape without issuing DDL. It fails when the schema is absent. This is the preferred steady-state mode when deployment and runtime identities are separated.

The provider rejects:

- a partial or unversioned provider schema;
- unsupported older or future versions;
- incompatible columns, binary key collations, keys, constraints, foreign keys, or indexes;
- databases with `READ_COMMITTED_SNAPSHOT ON`.

The version-1 lease query relies on `UPDLOCK`, `READPAST`, and `ROWLOCK` under locking read committed. The isolation requirement is checked on first use instead of being silently changed by the library.

## Permissions

A deployment identity using `CreateOrMigrate` needs permission to connect to the configured database, acquire the provider application lock, inspect metadata, and create provider-owned tables, constraints, and indexes in `dbo`. A typical least-privilege grant includes `CONNECT`, `VIEW DEFINITION`, `CREATE TABLE`, and the required `ALTER` permission on the target schema. Exact grants remain a database-administration decision.

A runtime identity using `ValidateOnly` needs `CONNECT`, metadata visibility, application-lock access, and `SELECT`, `INSERT`, and `UPDATE` on the three provider-owned tables. The provider does not need `DELETE` for normal capture, delivery, dead-letter, or replay operations.

Use separate identities when operational policy requires runtime DDL to be impossible. The library never creates the configured database or changes database isolation settings.

## Delivery and failure semantics

Capture is idempotent by ordinal application address plus message ID. Equivalent content returns `AlreadyExists`; different content for the same key returns `Conflict`. Capture uses a serializable key-range transaction.

Delivery is at-least-once. Leasing uses deterministic ordering and database locks so competing workers do not receive the same active lease. Renewal, complete, retry, dead-letter, and replay use compare-and-set predicates protecting state, lease token, expiry, and dead-letter generation. Renewal changes only the exact current unexpired lease's expiry columns and requires no schema change. A process or network failure around commit can still leave the caller uncertain whether a state change committed. Downstream handlers and sinks must therefore be idempotent.

The provider does not automatically retry state-changing commands or transactions. Retrying after an ambiguous commit could lease a different row or report a false transition result. Explicit bounded connection resiliency settings are delegated to the official client; higher-level retry policy belongs to the host and must respect operation semantics.

Provider validation rejects identifiers that cannot fit the schema before opening the database: addresses over 300 characters, message IDs over 128, contract names over 1,024, lease owners over 512, and trace/lineage identifiers over 512. Payloads, headers, error messages, and error details use unbounded text columns and receive no artificial provider size limit.

## Operations and ownership

- Connections are operation-scoped and use the SQL client's normal pool. The store owns no global pool and does not clear pools when disposed.
- Credentials and rotation are host-owned. Never place a literal credential in source, logs, documentation, or application documents.
- The three provider tables, their indexes, backups, encryption, retention policy and scheduling, capacity, monitoring, and disaster recovery are host/database-operator responsibilities. FluxFlow exposes only an explicit bounded terminal-deletion primitive.
- Stable outer configuration and schema messages avoid connection strings, payloads, headers, and executable SQL. The original database exception can remain available as an inner exception for controlled diagnostics.
- This package supplies storage only. Use the core durable-output registration for capture selection and delivery orchestration.

## Real-server validation

The explicit integration project is outside the normal solution test path so normal builds do not require Docker or a network. Its runner can create an ephemeral official SQL Server 2022 Linux container or use an externally managed test database. Container execution requires explicit license acceptance, uses unique temporary resources, prints no credential, runs with zero skipped cases, and removes the container in a `finally` block by default.

See the runner README in `tests/FluxFlow.Engine.DurableOutput.TSql.IntegrationTests` for the exact command and the currently validated image digest.

## Related pages

- [Optional Durable Output Capture](27-durable-output-capture.md)
- [Optional Durable Output Delivery](29-durable-output-delivery.md)
- [Durable Output Dead-Letter Operations](30-durable-output-dead-letter-operations.md)
- [SQL-File Durable Outputs](28-sql-file-durable-outputs.md)
- [Networked Relational Durable-Output Feasibility](31-networked-relational-durable-output-feasibility.md)
- [Durable Terminal Retention](36-durable-terminal-retention.md)
- [Durable Output Lease Renewal](37-durable-output-lease-renewal.md)
