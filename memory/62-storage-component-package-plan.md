# Storage Component Package Plan

Date: 2026-06-01

## Decision

Plan `FluxFlow.Components.Storage` as the next generic component package.

This should be a host-adapter-backed package for durable key/value or document
style workflow storage. It must stay general-purpose and must not absorb
product workspace, dashboard, session browser, scenario, or UI concepts.

## Why Storage Next

Storage is the next reusable primitive after mapping, control, validation,
timers, observability, sessions, metrics, and state.

Useful flows need a durable boundary for:

- remembering seen items across runs
- deduplication and idempotency
- request/response correlation
- storing intermediate workflow outputs
- loading reference data for later steps
- writing audit records without coupling to a product database

The package should provide the workflow node behavior and neutral contracts.
The host should provide the actual store adapter.

## v0.1 Scope

Package: `FluxFlow.Components.Storage`

Nodes:

- `storage.put`
- `storage.get`
- `storage.delete`

Ports:

- `storage.put`: `Input` -> `Result`, `Errors`
- `storage.get`: `Input` -> `Result`, `Found`, `NotFound`, `Errors`
- `storage.delete`: `Input` -> `Result`, `Errors`

Contracts:

- `StoragePutRequest`
- `StorageGetRequest`
- `StorageDeleteRequest`
- `StorageResult`
- `StorageRecord`
- `StorageWriteMode`
- `IStorageStore`
- `IStorageStoreFactory`
- `StorageStoreContext`
- `StorageStoreLease`

## Request Shape

Suggested common fields:

- `Collection`
- `Key`
- `Value`
- `ContentType`
- `Attributes`
- `ExpectedVersion`
- `ExpiresAt`
- `CorrelationId`

`Value` can be `object?` for v0.1 because the host store adapter owns
serialization. Hosts that need explicit binary or text storage can map into
records with `ContentType` and selected attributes, or use serialization nodes
before storage.

## Options

Common node options:

- `store`
- `collection`
- `boundedCapacity`

Put options:

- `mode`: `upsert`, `create`, `replace`
- `emitStoredRecord`

Get options:

- `includeExpired`

Delete options:

- `emitMissingAsResult`

## Store Boundary

The package defines `IStorageStore`, but does not ship a concrete database.

The factory receives enough context to resolve a host-owned store:

- node address
- store name
- default collection
- cancellation token

Store ownership must be explicit. A package-created store can be disposed by
the package; a host-shared store must not be disposed accidentally.

## Runtime Behavior

Expected behavior:

- ordered per-node processing
- bounded input and output capacity
- cancellation-aware store calls
- per-message store failures emit `FlowError`
- later messages continue after per-message failures
- startup fails clearly when the store cannot be opened
- missing get results route to `NotFound`, not `Errors`
- delete of a missing record is configurable, not a hard failure
- diagnostics include operation, store, collection, key, version, and
  correlation id when present

## Non-Goals

Do not include in v0.1:

- concrete database implementation
- workspace storage schema
- dashboard widgets
- session browser behavior
- scenario or test runner behavior
- query language
- transactions
- migrations
- distributed locks
- persistent state reducer integration

## Relationship To Existing Packages

- `FluxFlow.Components.Sessions` keeps its session-specific store abstraction.
  It should not migrate until storage proves the shared abstraction is useful.
- `state.reducer` remains in-memory for now. A future version can add an
  optional persistent state store after the storage package settles.
- `FluxFlow.Components.FileSystem` remains file operation focused. Storage is
  a logical record abstraction, not a path abstraction.
- Serialization and payload packages remain the right place for converting
  values before storage.

## Acceptance For v0.1

- Package source project and test project exist.
- `storage.put`, `storage.get`, and `storage.delete` register through
  `RegisterStorageComponents`.
- No concrete database dependency.
- Host-injected in-memory fake store is enough for tests.
- Put supports create, replace, and upsert behavior.
- Get routes found and missing records separately.
- Delete returns whether a record existed.
- Store failures produce `FlowError` and later messages continue.
- Startup failure is clear when factory/open fails.
- Bounded capacity is validated.
- Diagnostics cover success and failure paths.
- README documents contracts, options, registration, and host ownership.

## Suggested Development Steps

1. Add package and test projects.
2. Add contracts, options, error codes, diagnostic names, and registration.
3. Implement store lease/factory abstraction.
4. Implement `storage.put`.
5. Implement `storage.get`.
6. Implement `storage.delete`.
7. Add tests for success, missing records, modes, failures, startup failure,
   capacity validation, completion, and diagnostics.
8. Add README, changelog entry, and release metadata.
9. Run full solution tests.
10. Review with the same architecture review loop before release tagging.

## Open Questions

- Should `StorageRecord.Value` remain `object?`, or should v0.1 force byte
  payloads plus content type?
- Should `Collection` be required per request, or can a node default be enough?
- Should optimistic concurrency be included in v0.1 through `ExpectedVersion`,
  or left as metadata until a concrete consumer needs it?
- Should expiration be owned by storage package contracts, or should the host
  store decide retention policy?
