# vNext Storage FlowContent And Results

Date: 2026-07-20

## Status

The twenty-ninth bounded vNext milestone is implemented on local branch
`work/storage-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone makes exact FlowContent records and typed normal operation
results the canonical Storage Composition surface. Released typed nodes,
contracts, store interfaces, factories, and concrete adapters remain available
without behavior or ownership changes.

## Canonical Runtime

- `StorageContentPutRequest` carries exact FlowContent plus collection, key,
  attributes, optimistic-write, expiration, and mode data.
- `FlowContentStoragePutNode`, `FlowContentStorageGetNode`,
  `FlowContentStorageQueryNode`, and `FlowContentStorageDeleteNode` expose one
  Input, one broadcast Output, Events, and Completion. Outputs are strong
  `FlowResult<T>` contracts with stable `StorageResultKinds` and
  `StorageErrorCodeNames`; there is no universal Errors data port.
- Missing get/delete records are normal non-error outcomes. Invalid requests,
  unavailable value-only content, malformed stored content, optimistic-write
  failures, and backend failures are ordinary error results. Accepted later
  inputs continue after operation failures.
- Result messages preserve correlation, trace, headers, and causation. Record
  attributes, record lists, exact bytes, content type, and encoding are copied
  into immutable result snapshots.
- Canonical content crosses the existing `StorageRecord.Value` boundary in a
  private versioned JSON-compatible envelope. The envelope stores exact bytes
  as base64 plus content metadata and is not a workflow or host contract.
- Existing typed `StoragePutNode`, `StorageGetNode`, `StorageQueryNode`, and
  `StorageDeleteNode` remain unchanged.

## Stores And Adapters

- Canonical nodes continue to borrow host-owned `IStorageStore` instances.
  `IStorageStoreFactory`, `StorageStoreLease`, and `StorageStoreContext` retain
  their released contracts and ownership rules.
- FileSystem and SqlFile adapter source and package versions are unchanged.
  Integration regressions prove both adapters round-trip canonical exact bytes,
  content metadata, record metadata, and query records through the private
  envelope.
- Custom stores used with canonical nodes must preserve JSON object values and
  record metadata. Legacy or malformed values return
  `storage.stored_content_invalid` as normal data rather than faulting the
  workflow.

## Composition And Designer

- Parameterless `storage.put`, `storage.get`, `storage.query`, and
  `storage.delete` registrations now own canonical one-output descriptors.
  Factory-backed stores are still opened at composition build time and disposed
  with the composed node; direct keyed stores remain host-owned.
- Explicit `RegisterStoragePutResult(...)`,
  `RegisterStorageGetResultBranches(...)`,
  `RegisterStorageQueryRecordOutputs(...)`, and
  `RegisterStorageDeleteResult(...)` methods preserve released typed ports
  under caller-selected node types.
- Designer metadata describes canonical fixed ports and explicitly omits the
  typed-only query record-output and missing-delete suppression options.
- Package examples use flat `Resources` / `Workflows` documents and an explicit
  upstream serializer plus request builder. Storage performs no implicit
  serialization or mapping.
- Release source conventions now inspect all package composition implementation
  files for resource and port use while default registry discovery remains
  scoped to `*CompositionNodeRegistryExtensions` classes. This permits explicit
  compatibility registration/factory files without making them default nodes.

## Compatibility And Versioning

- `FluxFlow.Components.Storage` moves from `3.0.10` to `4.0.0` for the additive
  canonical FlowContent/result surface while preserving released declarations.
- `FluxFlow.Components.Storage.Composition` moves from `1.5.0` to `2.0.0`
  because default output types and branch/Error port surfaces change.
- The source-declaration baseline changes only manifest entries 52 and 53,
  corresponding to Storage runtime and Storage Composition.
- SDK package validation passes against published Storage `3.0.10` and Storage
  Composition `1.5.0`. Release preflight and complete dry-runs pass for both
  packages against the seeded temporary current-package source.

## Verification

- Storage runtime tests: 70 passed.
- Storage Composition tests: 20 passed.
- FileSystem storage adapter tests: 30 passed.
- SqlFile storage adapter tests: 31 passed.
- Storage adapter registration tests: 4 passed.
- Core Composition tests: 126 passed.
- Composition Hosting tests: 38 passed.
- Designer tests: 98 passed.
- Release convention tests: 93 passed.
- The complete Release no-build sweep passed 2,136 tests across 63 projects
  with no failures or warnings.
- Controlled Debug and Release builds completed all 130 projects with zero
  warnings and zero errors.
- A package-only net8 consumer restored Storage `4.0.0` and Storage Composition
  `2.0.0` from the external current-package source, compiled canonical
  FlowContent commands and canonical/typed registrations, and printed
  `STORAGE_PACKAGE_CONSUMER_OK`.

## Deferred Boundaries

- Storage does not infer serializers, codecs, JSON objects, or text encodings.
  Serialization and Mapping own explicit value-to-content conversion.
- This pass adds no store adapter, store ownership, polling, durable event
  journal, runtime reload, host lifecycle, renderer, or engine dependency.
- Legacy typed Storage Composition `1.x` remains the stored-definition
  compatibility line.

## Next Gate

Assess Sessions as the next bounded component-family pass. Preserve host-owned
session stores and released direct-use contracts while deciding which session
values and expected outcomes should move to canonical FlowValue and normal
results.
