# vNext Data Foundation

Date: 2026-07-17

## Status

The first bounded vNext milestone is implemented on local branch
`work/data-foundation-vnext`. The foundation API review is complete and
accepted in `206-vnext-data-foundation-api-review.md`.
No push, tag, package publication, pull request, or merge was performed.

Implementation stops at the data and message-contract boundary. JSON workflow
configuration revisions, runtime registration/reload changes, component
migration, diagnostics ports, and the MQTT redesign remain deferred until the
next bounded milestone.

## Package Boundaries

- Added Dataflow-free package `FluxFlow.Data` `1.0.0` and its test project.
- Bumped `FluxFlow.Nodes` to `2.0.0` because its public `FlowMessage<T>` envelope
  now uses the vNext data and identity contracts.
- Added `data` to `eng/packages.json`; the manifest now contains 58 packages.
- `FluxFlow.Data` owns reusable values, content decoding, and result contracts.
- `FluxFlow.Nodes` remains the owner of the traceable message envelope.
- Existing component packages and runtime packages were not migrated in this
  milestone.

## Contracts

### FlowValue

- `FlowValue` is an immutable discriminated value covering null, Boolean,
  integer, decimal, floating point, string, binary, temporal values, GUID,
  arrays, and ordinal-keyed objects.
- Mutable input collections and byte arrays are copied at ingress.
- Structural equality is order-sensitive for arrays and order-independent for
  objects; numeric kinds remain distinct.
- Canonical JSON uses an explicit kind/value representation, invariant numeric
  text, and sorted object keys so round trips are lossless and deterministic.
- Natural transport JSON is deliberately handled by a content codec rather
  than by the canonical persistence format.

### FlowContent

- `FlowContent` retains an immutable copy of original bytes and lazily decodes
  a `FlowValue` once, caching either the decoded value or the decoding failure.
- Codec selection is deterministic: exact media type, structured suffix,
  media family, then binary fallback.
- Built-in JSON, text, and binary codecs are provided. XML remains deferred to
  the later Serialization migration.
- Text decoding honors valid declared charsets and falls back to UTF-8 for
  missing or unsupported charset values.

### FlowMessage

- `FlowMessage<T>` now has strong `TraceId` and `MessageId` value types plus an
  optional `CausationId`.
- Headers are an immutable, ordinal `IReadOnlyDictionary<string, FlowValue>`.
- `With(...)` preserves correlation, trace, and headers while creating a new
  message identity and recording the parent message as causation.
- JSON serialization round trips the envelope and canonical header values.

### Results

- Added `IFlowResult`, `FlowResult<T>`, and Data-owned `FlowError` contracts.
- Success and error are result shapes on the normal output path; `IsError` is
  derived from the presence of `Error` rather than stored independently.
- Error details use `FlowValue`; raw exception instances do not cross workflow
  or persistence boundaries.
- The older Nodes-owned error contract remains in place until component
  migration is planned separately.

## Documentation

- Added `docs/19-vnext-runtime-architecture.md` for package ownership,
  architectural boundaries, invariants, and staged migration.
- Added `docs/20-flow-data-contracts.md` for value, content, message identity,
  and result semantics.
- Updated the docs index, node-authoring guide, public API overview, package
  READMEs, changelog, manifest, solution, and public API baseline.

## Verification

- `FluxFlow.Data.Tests`: 32 passed after API-review hardening.
- `FluxFlow.Nodes.Tests`: 41 passed after API-review hardening.
- `FluxFlow.Release.Tests`: 93 passed, including the Data boundary guard.
- The complete Release test sweep passed with no failures or skips.
- Controlled Debug solution build passed with 0 warnings and 0 errors.
- Controlled Release solution build passed with 0 warnings and 0 errors.
- `FluxFlow.Data` `1.0.0` and `FluxFlow.Nodes` `2.0.0` packed successfully to a
  temporary directory outside the repository.
- The Nodes package declares the expected `FluxFlow.Data` `1.0.0` dependency.
- Release preflight passed for aliases `data` `1.0.0` and `nodes` `2.0.0`.
- Isolated package dry-runs passed for Data and Nodes, including archive
  inspection and temporary net8 consumer restore/build against the local
  package source.
- A timed-out build left workspace-owned build processes; only those stale
  processes were stopped before `dotnet build-server shutdown` and successful
  controlled reruns.

## Next Gate

The public foundation contracts are accepted. The next bounded milestone may
move to canonical Composition definitions and addressing. Link conditions,
runtime/DI changes, component adapters, diagnostics events, and MQTT resources
and components remain later milestones.
