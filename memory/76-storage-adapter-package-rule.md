# Storage Adapter Package Rule

Date: 2026-06-02

## Decision

Keep `FluxFlow.Components.Storage` as the logical workflow package only.

Every reusable persistence implementation must be a separate adapter package
under:

```text
FluxFlow.Components.Storage.*
```

## Rule

The base storage package owns:

- workflow nodes
- request and result contracts
- `IStorageStore`
- `IStorageStoreFactory`
- store leases and context contracts
- options and diagnostics for the logical storage nodes

The base storage package must not own concrete persistence implementations.

Each adapter package owns one persistence style:

- store implementation
- optional store factory
- registration helpers
- adapter-specific options
- adapter-specific tests and README content

Adapter packages should not add workflow nodes. If an adapter seems to need a
new node type, first check whether the base storage contracts need a neutral
extension.

## Naming

Use neutral package suffixes:

```text
FluxFlow.Components.Storage.Local
FluxFlow.Components.Storage.EmbeddedDocument
FluxFlow.Components.Storage.EmbeddedSql
FluxFlow.Components.Storage.ServerSql
```

Avoid naming adapter packages after one consuming application or one product
workflow.

## Current Status

`FluxFlow.Components.Storage.Local` already follows the rule:

- it is a separate source project
- it is adapter-only
- it adds no workflow nodes
- it exposes `LocalStorageStore`, `LocalStorageStoreFactory`, options, and
  registration helpers

## Next

Do not add another storage adapter until a real workflow proves the need.
When that need appears, create a separate `FluxFlow.Components.Storage.*`
package instead of extending the base storage package or the local adapter.
