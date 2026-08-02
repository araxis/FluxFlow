# Durability Operational Status

Date: 2026-08-01

## Outcome

FluxFlow durable input and output expose separate optional read-only status
capabilities. Hosts can inspect backlog, lease, terminal, and dead-letter state
without querying provider-owned tables or changing application definitions.

## Contracts

- `IDurableInputStatusStore` accepts an immutable
  `DurableInputStatusQuery` with explicit caller-owned `ObservedAt` and returns
  `DurableInputStatusSnapshot`.
- Input snapshots contain pending/ready, leased/expired, delivered, and
  dead-letter counts plus oldest-ready, next-active-expiry, and checked total
  metadata.
- `IDurableOutputStatusStore` accepts the equivalent output query and returns
  `DurableOutputStatusSnapshot`.
- Output snapshots distinguish total captures, unmaterialized/ready captures,
  pending/ready delivery, leased/expired delivery, completed tombstones, and
  dead letters. Checked tracked-delivery and ready totals make count
  relationships explicit.
- Both snapshots are immutable and contain no address, key, contract, payload,
  header, trace identity, lease owner/token, failure description, or exception.

## Provider Behavior

- SQL-file and T-SQL input/output stores implement status on the existing
  container-owned singleton and register one additional exact interface alias.
- Registration and resolution remain I/O-free, equivalent registration remains
  idempotent, and partial/tampered ownership fails atomically.
- Status skips normal lazy schema initialization. It neither creates nor
  migrates schema and never changes, leases, settles, backfills, or replays a
  record.
- SQL-file status uses an unpooled read-only operation-scoped connection so no
  second connection pool retains a temporary or host-owned database file.
- SQL-file output probes delivery-table existence read-only. When capture state
  exists without delivery schema, every capture is reported as unmaterialized
  and the delivery table remains absent.
- T-SQL status uses the provider's bounded pooled connection-open and command
  timeout paths with one aggregate command and no explicit write transaction.
- Undefined state values and orphan output-delivery rows fail visibly instead
  of being omitted from counts.

## Boundaries

This round adds no retention, purge, archive, health-check registration,
metrics exporter, polling worker, timer, cache, endpoint, UI, transport,
workflow checkpoint, exactly-once guarantee, distributed transaction, provider
discovery, new configuration, schema version, or new dependency. Verification
upgraded the existing SQLite native bundle from 2.1.11 to the compatible
patched 2.1.12 line after an external-consumer advisory; final provider scans
reported no vulnerable packages.

The six packages advance by one additive minor version: input core/SQL-file to
1.2.0, input T-SQL to 1.1.0, output core/SQL-file to 2.1.0, and output T-SQL to
1.1.0.

## Validation Evidence

- The focused local operational-status matrix passed 102 tests, and six
  release package-version assertions passed.
- Real SQL Server suites passed input 77/77 and output 87/87 with no failures
  or skips against image digest
  `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`;
  their owned containers were removed.
- Serialized Debug and Release solution builds covered 133 projects without
  errors or warnings. The default Release suite passed 2,358/2,358 tests in 66
  projects and release governance passed 117/117.
- After the dependency patch, Release rebuilt without warnings, complete
  SQL-file provider projects passed input 111/111 and output 134/134, and both
  provider dependency scans reported no vulnerable packages.
- All six package/symbol archives passed inspection, feed verification, and
  isolated consumer execution on `net8.0` and `net10.0`. Binary-compatibility
  commands were prepared for all six expected baselines; actual comparison is
  unavailable because the package families are not published and only current
  dry-run artifacts remain locally.
- Public API, full-solution formatting, diff hygiene, forbidden-pattern,
  assertion-quality, test-gap, pseudo-mutation, temporary-cache, and container
  cleanup checks passed.

The authoritative accepted scope and detailed completion evidence are in
`goals/2026-08-01-durability-operational-status/README.md`.
