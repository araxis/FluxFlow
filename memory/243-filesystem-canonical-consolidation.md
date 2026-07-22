# FileSystem Canonical Consolidation

Date: 2026-07-22

## Status

The FileSystem family consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, publication, pull request, or
merge was performed.

This milestone supersedes the additive compatibility state recorded in
`231-vnext-filesystem-flowcontent-results.md`. FileSystem now has one maintained
runtime and Composition surface.

## Runtime Consolidation

- `FileReadNode` is the exact-byte `FileReadRequest` to
  `FlowResult<FileReadContent>` transform.
- `FileWriteNode` is the exact-byte `FileContentWriteRequest` to
  `FlowResult<FileWriteResult>` transform.
- `DirectoryEnumerateNode` and `FileWatchNode` directly emit immutable
  `FlowValue` objects without typed-source projection wrappers.
- Expected read/write failures remain normal Output values. Source
  infrastructure failures fault Completion and lifecycle diagnostics remain on
  Events.
- The direct source pipeline preserves bounded output, pre-canceled startup,
  normal completion, fault propagation, and disposal. File watcher ownership is
  cleared before failed activation cleanup so later shutdown cannot retain a
  disposed watcher.
- Path confinement, descendant reparse-point rejection, bounded reads,
  exact-byte ownership, write modes, timestamps, diagnostics, and message
  lineage remain covered.

## Removed Compatibility Surface

- Removed `FileReadResult`, `FileWriteRequest`, `DirectoryEnumerateEntry`,
  `DirectoryEntryType`, `FileWatchEvent`, and `FileWatchChangeType`.
- Removed the former typed implementations and the temporary
  `FlowContentFileReadNode`, `FlowContentFileWriteNode`,
  `FlowValueDirectoryEnumerateNode`, and `FlowValueFileWatchNode` names.
- Removed the source projection shim and typed Composition registration
  extensions.
- Consolidated compatible tests on the concise canonical nodes before deleting
  duplicate suites.
- The cleanup ledger now records FileSystem as `removed-after-parity` and keeps
  Storage as a separate pending behavior-parity item.

## Versioning And Compatibility

- `FluxFlow.Components.FileSystem` moves from `4.0.0` to `5.0.0`.
- `FluxFlow.Components.FileSystem.Composition` moves from `2.2.0` to `3.0.0`.
- Source-declaration baseline entries changed only for manifest indexes 25 and
  26, from 254 to 202 and 23 to 18 declarations respectively.
- SDK package validation against FileSystem `4.0.0` reports the six removed
  contracts, the four deliberately changed concise node base contracts, and
  the four removed temporary canonical names on both target frameworks.
- SDK package validation against FileSystem Composition `2.2.0` reports only
  removal of `FileSystemTypedRegistrationExtensions` on both target
  frameworks. No suppression was generated.

## Verification

- FileSystem runtime tests: 43 passed.
- FileSystem Composition tests: 26 passed.
- Core Composition tests: 145 passed.
- Composition Hosting tests: 46 passed; only the existing legacy-API migration
  warnings were emitted.
- Designer tests: 112 passed.
- Release tests: 96 passed.
- Controlled Debug build: succeeded with 55 existing warnings and no errors.
- Controlled Release build: succeeded with 93 existing warnings and no errors.
- A fresh temporary source outside the repository was seeded with all 58
  current manifest packages.
- Release preflight and dry-run passed for both affected packages, including
  archive inspection, feed resolution, and isolated `net8.0` smoke consumers.
- A combined package-only `net8.0` consumer restored FileSystem `5.0.0` and
  FileSystem Composition `3.0.0` from that source and built with warnings as
  errors.

## Next Gate

Consolidate Storage on its concise canonical content/result nodes. Preserve
host-owned stores and factories, record expiration, adapter integration, and
normal result behavior before removing its typed contracts, branch outputs,
Errors ports, and typed Composition registrations.
