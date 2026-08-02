# Storage Canonical Consolidation

Date: 2026-07-23

## Status

The Storage family consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, publication, pull request, or
merge was performed.

This milestone supersedes the additive compatibility state recorded in
`232-vnext-storage-flowcontent-results.md`. Storage now has one maintained
component-facing runtime and Composition surface while retaining its public
backend-adapter boundary.

## Runtime Consolidation

- `StoragePutNode` accepts `StorageContentPutRequest` and emits
  `FlowResult<StoragePutOutcome>`.
- `StorageGetNode`, `StorageQueryNode`, and `StorageDeleteNode` retain their
  store request inputs and emit one normal `FlowResult<TOutcome>` output.
- Expected validation, backend, missing-content, and stored-content failures
  remain ordinary output values. Missing get/delete results are successful
  result variants.
- Exact bytes and metadata still use the private versioned content envelope;
  write modes, expected versions, expiration, filtering, paging, bounded
  processing, diagnostics, fan-out, completion draining, and message lineage
  remain covered.
- The duplicated internal node helpers were reduced to one normalization,
  result, error, and diagnostic support layer.

## Removed Compatibility Surface

- Removed the temporary `FlowContentStoragePutNode`,
  `FlowContentStorageGetNode`, `FlowContentStorageQueryNode`, and
  `FlowContentStorageDeleteNode` names after their implementations assumed the
  concise names.
- Removed the former typed component implementations, `StorageQueryResult`,
  numeric `StorageErrorCodes`, branch and Errors outputs, legacy-only
  `EmitRecordOutputs` and `EmitMissingAsResult` options, and typed Composition
  registration extensions.
- Removed `Found`, `NotFound`, and `Records` Composition port constants.
- Preserved `IStorageStore`, `IStorageStoreFactory`, `StorageStoreLease`,
  `StorageStoreContext`, `StoragePutRequest`, `StorageRecord`, and
  `StorageResult` because FileSystem, SqlFile, and custom stores implement that
  host-owned adapter boundary.
- Migrated Storage Composition tests from obsolete Composition hosting to the
  canonical application revision and stable-port runtime.

## Versioning And Compatibility

- `FluxFlow.Components.Storage` moves from `4.0.0` to `5.0.0`.
- `FluxFlow.Components.Storage.Composition` moves from `2.1.0` to `3.0.0`.
- Concrete FileSystem and SqlFile adapter source and package versions remain
  unchanged.
- Source-declaration baseline entries changed only for manifest indexes 52 and
  53, from 297 to 255 and 31 to 23 declarations respectively.
- SDK package validation against Storage `4.0.0` reports only the removed
  compatibility types/members and the four intentional concise-node contract
  changes on both target frameworks.
- SDK package validation against Storage Composition `2.1.0` reports only the
  typed registration type and three branch-port constants on both target
  frameworks. No suppression was generated.

## Verification

- Storage runtime tests: 33 passed.
- Storage Composition tests: 18 passed with canonical hosting and no warnings.
- FileSystem adapter tests: 30 passed.
- SqlFile adapter tests: 31 passed.
- Shared adapter registration tests: 4 passed.
- Core Composition tests: 145 passed.
- Composition Hosting tests: 46 passed; only 11 existing legacy migration
  warnings were emitted.
- Designer tests: 112 passed.
- Release tests: 96 passed.
- Controlled Debug build: succeeded with 51 existing warnings and no errors.
- Controlled Release build: succeeded with 89 existing warnings and no errors.
- A fresh temporary source outside the repository was seeded with all 58
  current manifest packages.
- Release preflight and local-source dry-run passed for both affected packages,
  including archive, isolated smoke consumer, and dependency-feed checks.
- A combined package-only `net8.0` consumer restored Storage `5.0.0`, Storage
  Composition `3.0.0`, and both unchanged concrete adapters, then built with
  warnings as errors.

## Next Gate

Consolidate Mapping and Validation as separate bounded passes. Preserve their
typed expression/schema capabilities until equivalent canonical FlowValue
behavior and migration coverage are proved.
