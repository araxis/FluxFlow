# vNext Sessions FlowContent And Results

Date: 2026-07-20

## Status

The thirtieth bounded vNext milestone is implemented on local branch
`work/sessions-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone makes exact FlowContent recording/replay and typed normal
operation results the canonical Sessions Composition surface. Released typed
nodes, payload contracts, store interfaces, factories, leases, and ownership
rules remain available without behavioral changes.

## Canonical Runtime

- `SessionContentRecordInput` and `SessionContentRecord` carry exact immutable
  FlowContent plus copied record metadata.
- `SessionContentRecorderNode` exposes one Input, one
  `FlowResult<SessionContentRecord>` Output, Events, Completion, and the
  recorder-specific `SessionCompleted` close task. It opens lazily after
  content validation and closes after accepted input/output drain.
- `SessionContentReplayNode` is a source of
  `FlowResult<SessionContentRecord>`. Missing sessions, store/read failures,
  and malformed stored records are normal outputs; malformed records do not
  prevent later records from replaying.
- `SessionContentQueryNode` returns one `FlowResult<SessionQueryOutcome>` with
  count and optional copied session metadata. It does not expose the typed-only
  Sessions branch.
- Stable `SessionResultKinds` and `SessionErrorCodeNames` make expected
  failures ordinary workflow data. Canonical nodes expose Events and no
  universal Errors data port.
- Recorder/query results preserve correlation, trace, headers, and causation.
  Replay outputs mint fresh source identity. Accepted messages remain ordered
  and later messages continue after normal failures.

## Store Boundary

- A private versioned JSON-compatible envelope preserves exact bytes, content
  type, and encoding through the existing object-valued
  `SessionRecord.Payload` boundary.
- The decoder accepts both the in-memory envelope and a serialized
  `JsonElement`; tests prove exact bytes and metadata survive JSON-object
  persistence.
- Value-only FlowContent is rejected as `session.content_unavailable`; no
  implicit serialization or mapping is added.
- Direct stores remain host-owned. Factory leases retain their existing
  open/dispose scope and receive the configured session id and clock.

## Composition And Compatibility

- Parameterless `RegisterSessionRecorder`, `RegisterSessionReplay`, and
  `RegisterSessionQuery` now register canonical fixed ports and Events only.
- `RegisterSessionRecordOutput`, `RegisterSessionReplayRecords`, and
  `RegisterSessionQueryResultBranches` preserve released typed contracts under
  explicit caller-selected node types, including legacy Errors and query
  Sessions branch behavior.
- Designer metadata describes canonical FlowContent/result types and marks
  typed-only `emitSessionOutputs` as omitted. Store/clock picker ownership and
  key patterns remain unchanged.
- Package examples use only flat top-level `Resources` and `Workflows` and make
  serialization/request construction explicit.

## Versions And API

- `FluxFlow.Components.Sessions` moved from `3.3.3` to `4.0.0`.
- `FluxFlow.Components.Sessions.Composition` moved from `1.6.0` to `2.0.0`.
- Public API baseline entries 48 and 49 changed only for the additive canonical
  contracts/nodes and explicit compatibility registration methods.
- Package release notes and the top-level changelog describe the major-version
  contract change. No store interface or released typed declaration was
  removed.

## Verification

- Sessions runtime tests: 60 passed, 0 warnings.
- Sessions Composition tests: 26 passed, 0 warnings.
- Core Composition tests: 126 passed, 0 warnings.
- Composition Hosting tests: 38 passed, 0 warnings.
- Designer tests: 98 passed, 0 warnings.
- Release tests: 93 passed, 0 warnings.
- Complete Release no-build sweep: 2,143 tests across 63 projects passed with
  0 warnings.
- Controlled Debug solution build passed for 130 projects with 0 warnings and
  0 errors. Controlled Release solution build passed; warning-only incremental
  audit and forced affected-package Release rebuilds reported 0 warnings and
  0 errors.
- SDK package validation passed for Sessions `4.0.0` against `3.3.3` and
  Sessions Composition `2.0.0` against `1.6.0`.
- Release preflight and isolated local-source dry-runs passed for both packages.
- A package-only net8 consumer restored the packed Composition package,
  round-tripped exact canonical content, checked causation, inspected canonical
  and typed port metadata, and printed `SESSIONS_PACKAGE_CONSUMER_OK`.
- Graphify refreshed local ignored output to 18,226 nodes, 28,704 edges, and
  1,829 communities.

## Next Boundary

The ordinary runtime component-family migration is complete through Sessions.
The next bounded assessment should cover the remaining resource/configuration
infrastructure (`Resources`, `Secrets`, and `Configuration`) and its nested
address/ownership alignment before the final Hosting and Designer persistence
passes. That work must remain separate from this commit.
