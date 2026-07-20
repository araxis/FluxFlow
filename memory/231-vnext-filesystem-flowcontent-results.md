# vNext FileSystem FlowContent And Results

Date: 2026-07-20

## Status

The twenty-eighth bounded vNext milestone is implemented on local branch
`work/filesystem-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone makes exact FlowContent file operations and FlowValue file
sources the canonical FileSystem Composition surface. Released typed nodes and
contracts remain available for direct use and explicit compatibility
registration.

## Canonical Runtime

- `FileReadRequest` can declare content type while retaining the released path,
  encoding, and read-mode contract. `FlowContentFileReadNode` returns
  `FlowResult<FileReadContent>` with exact original bytes, content metadata,
  read facts, stable result kinds, and stable string error codes.
- `FileContentWriteRequest` carries exact FlowContent. The canonical writer
  writes only original bytes and returns `FlowResult<FileWriteResult>`; a
  value-only body is a normal `content_unavailable` result so serialization
  remains an explicit upstream concern.
- Expected path, confinement, encoding, mode, access, not-found, size, content,
  and I/O failures are ordinary Output results. Accepted later inputs continue,
  and output envelopes preserve correlation, trace, headers, and causation.
- Existing bounded reads and base-directory confinement remain authoritative.
  Byte reads default to `application/octet-stream`; text-mode reads retain
  declared encoding as metadata without hidden decoding.
- `FlowValueDirectoryEnumerateNode` and `FlowValueFileWatchNode` project
  released source records to immutable FlowValue objects. Ordinary source
  failures are isolated as Completion faults rather than exposed through a
  universal Errors data port.
- Existing typed `FileReadNode`, `FileWriteNode`, `DirectoryEnumerateNode`, and
  `FileWatchNode` behavior and public contracts remain unchanged.

## Composition And Designer

- Parameterless registrations now own the canonical descriptors: `file.read`
  accepts `FileReadRequest` and emits `FlowResult<FileReadContent>`;
  `file.write` accepts `FileContentWriteRequest` and emits
  `FlowResult<FileWriteResult>`; directory enumeration and watch emit
  FlowValue. Canonical nodes expose Events and no universal Errors port.
- Explicit `RegisterFileReadResult(...)`, `RegisterFileWriteResult(...)`,
  `RegisterDirectoryEnumerateEntries(...)`, and
  `RegisterFileWatchEvents(...)` paths preserve released typed Composition
  contracts under distinct caller-selected node types.
- Designer metadata describes canonical fixed ports and omits the legacy write
  encoding option because exact-byte writes do not encode values.
- Package examples use the flat `Resources` / `Workflows` document and an
  explicit mapper or serializer node when a value must become bytes.

## Compatibility And Versioning

- `FluxFlow.Components.FileSystem` moves from local `3.1.3` to `4.0.0`; the
  latest published baseline is `3.1.2`. The major version records the additive
  canonical runtime surface while preserving all released declarations.
- `FluxFlow.Components.FileSystem.Composition` moves from `1.5.0` to `2.0.0`
  because its default port types and Errors surfaces change.
- Source-declaration baselines contain only intentional additive declarations
  and compatibility registrations. SDK package validation passes against
  published FileSystem `3.1.2` and Composition `1.5.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source outside tracked state.

## Verification

- FileSystem runtime tests: 66 passed.
- FileSystem Composition tests: 27 passed.
- Core Composition tests: 126 passed.
- Composition Hosting tests: 38 passed.
- Designer tests: 98 passed.
- Release convention tests: 93 passed.
- The complete Release no-build sweep passed 2,126 tests across 63 projects
  with no failures or warnings.
- Controlled Debug and Release builds completed all 130 projects with zero
  warnings and zero errors on their warm runs. The cold Release traversal
  exceeded its command window without compiler errors and left no FluxFlow
  build process.
- A package-only net8 consumer restored FileSystem `4.0.0` and FileSystem
  Composition `2.0.0`, asserted canonical registry contracts, executed exact
  byte write/read operations, verified result kinds and message lineage, and
  printed `FILESYSTEM_PACKAGE_CONSUMER_OK`.

## Deferred Boundaries

- FileSystem does not infer JSON, text, or object serialization. Mapping and
  Serialization own those explicit conversions.
- File watch and directory enumeration remain live push sources, not durable
  journals, polling APIs, or latest-value stores.
- Base-directory policy, file access, and watch lifetime remain host and node
  concerns; this pass adds no resource ownership or runtime reload behavior.
- Legacy FileSystem Composition `1.x` remains the stored-definition
  compatibility line.

## Next Gate

Assess Storage as the next bounded component-family pass. Migrate transported
record content to FlowContent and expected get/put/delete outcomes to normal
results while preserving host-owned stores and factories, concurrency
boundaries, and released direct-use compatibility.
