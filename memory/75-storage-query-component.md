# Storage Query Component

Date: 2026-06-02

## Decision

Add `storage.query` to `FluxFlow.Components.Storage` and query support to the
file-backed local storage adapter.

Query is a generic logical storage primitive. It stays neutral and does not
know any dashboard, workspace, scenario, or product storage schema.

## Scope

Added core package version `0.2.0-alpha.1` with:

- `storage.query`
- `StorageQueryRequest`
- `StorageQueryResult`
- `IStorageStore.QueryAsync(...)`
- `Input`, `Result`, `Records`, and `Errors` ports
- filters for collection, key prefix, exact-match attributes, stored time
  bounds, expired-record policy, and limit
- options for default collection, default limit, record payload inclusion,
  record output emission, and bounded capacity
- diagnostics for query completion and failures
- per-message query failures as `FlowError`

Added local adapter package version `0.2.0-alpha.1` with:

- `LocalStorageStore.QueryAsync(...)`
- scan-based query over existing local record files
- unchanged persisted record format
- deterministic output order by stored timestamp and key

## Behavior

`storage.query` receives `StorageQueryRequest` and emits one
`StorageQueryResult` per request. When enabled, it also emits each returned
`StorageRecord` on the `Records` port.

The node validates the collection and limit, applies configured defaults, and
continues after recoverable store failures.

The local adapter scans the current store directory and filters records without
using collection or key values as raw path segments.

## Verification

Added focused tests for:

- query summary result
- per-record output stream
- suppressed result payloads and record outputs
- query failure continuation
- node registration
- local adapter filters
- local adapter expired-record query behavior
- storage sample query workflow

## Next

Storage hardening can pause here. The next storage candidates are:

- retention/delete-by-query if a consumer needs cleanup policies
- separate indexed storage adapters if scan-based local query becomes too slow
- database adapters only when a real host workflow needs one
