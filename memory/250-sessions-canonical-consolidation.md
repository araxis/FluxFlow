# Sessions Canonical Consolidation

Date: 2026-07-23

## Status

The Sessions family consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, publication, pull request, or
merge was performed.

This milestone supersedes the additive compatibility state recorded in
`233-vnext-sessions-flowcontent-results.md`. Sessions now has one maintained
runtime and Composition contract while retaining the neutral store adapter
boundary.

## Canonical Contract

- `SessionRecorderNode` accepts `SessionContentRecordInput` and emits stored or
  failed `FlowResult<SessionContentRecord>` values through one Output.
- `SessionReplayNode` is a source of ordered
  `FlowResult<SessionContentRecord>` values with Instant, FixedInterval,
  RealTime, and Multiplier pacing.
- `SessionQueryNode` accepts `SessionQueryRequest` and emits one
  `FlowResult<SessionQueryOutcome>` containing the match count and optional
  copied session metadata.
- Exact bytes, content type, encoding, attributes, record order, deterministic
  clocks, diagnostics, correlation, trace, causation, and headers are
  preserved. Validation, store, missing-session, malformed-record, and query
  failures remain normal result data.
- Recorder creates sessions lazily and exposes `SessionCompleted` separately so
  a store close failure remains observable without changing normal accepted
  result delivery.
- `SessionRecordInput`, `SessionRecord`, `ISessionStore`,
  `ISessionStoreFactory`, `SessionStoreContext`, and `SessionStoreLease` remain
  the public adapter boundary. Nodes encode exact content through a private
  versioned store envelope.
- Recorder and query defaults use `SessionName`; this prevents canonical
  component identity derived from the workflow object key from becoming an
  unintended exact-name query filter.
- Composition retains `session.record`, hidden alias `session.recorder`,
  `session.replay`, and `session.query`, with fixed Input/Output/Events ports.
  Required stores and optional clocks use exact `Resources.{name}` addresses.

## Removed Compatibility Surface

- Removed the temporary `SessionContentRecorderNode`,
  `SessionContentReplayNode`, and `SessionContentQueryNode` names after their
  implementations assumed the concise public names.
- Removed the old direct-result `SessionRecorderNode`, `SessionReplayNode`, and
  `SessionQueryNode` implementations, their Errors streams, diagnostic constant
  members, and the query `Sessions` branch.
- Removed `SessionQueryResult`, `SessionComponentOptions`, numeric
  `SessionsErrorCodes`, typed Composition registration extensions, and the
  legacy Sessions port constant.
- Removed dead `Store` option properties and the query-only
  `EmitSessionOutputs` option. Resource selection now has one authoritative
  path through the required Composition resource.
- Renamed recorder/query option `Name` to `SessionName`; request/store contract
  `SessionQueryRequest.Name` remains the exact query filter.
- Migrated Composition tests from the obsolete Composition runtime to flat
  canonical application definitions, revision hosting, stable ports, and exact
  resource addresses.

Consumers now use the concise nodes, inspect `FlowResult.Kind`, `IsError`, and
`Error.Code`, read successful values from `Value`, replace Errors/Sessions links
with normal result conditions, and configure the store only through its
resource property.

## Versioning And Compatibility

- `FluxFlow.Components.Sessions` moves from `4.0.0` to `5.0.0`.
- `FluxFlow.Components.Sessions.Composition` moves from `2.2.0` to `3.0.0`.
- Source-declaration baseline manifest index 48 changed from 273 to 217 public
  declarations; Composition index 49 changed from 27 to 22.
- SDK package validation against runtime `4.0.0` reports only intentional
  removals and base/member changes for duplicate nodes, compatibility result
  and option types, numeric errors, branch/Error surfaces, and dead options on
  both target frameworks.
- SDK package validation against Composition `2.2.0` reports only intentional
  removal of `SessionsTypedRegistrationExtensions` and
  `SessionsCompositionPortNames.Sessions` on both target frameworks.
- No API compatibility suppression was generated.

## Verification

- Sessions runtime tests: 47 passed with no warnings.
- Sessions Composition tests: 25 passed through canonical hosting with no
  warnings.
- Core Composition tests: 145 passed.
- Composition Hosting tests: 46 passed with 11 existing migration warnings.
- Designer tests: 112 passed.
- Release tests: 96 passed.
- Controlled Debug build: succeeded with 19 warnings and no errors.
- Controlled Release build: succeeded with 57 warnings and no errors.
- A fresh temporary source outside the repository was seeded with all 58
  current manifest packages.
- Release preflight and local-source dry-run passed for both changed packages,
  including archive inspection, isolated smoke restore/build, and feed checks.
- A package-only `net8.0` consumer restored Sessions `5.0.0` and Sessions
  Composition `3.0.0`, built with warnings as errors, loaded flat
  `Resources`/`Workflows` JSON, verified canonical metadata and removed
  branches, exercised exact-content recording and session completion, preserved
  message lineage, and printed `SESSIONS_CANONICAL_API_OK`.
- The ignored local graph was refreshed after implementation and memory
  updates.

## Next Gate

Audit Timers independently. Preserve interval/schedule source lifecycle,
delay/throttle/debounce timing and completion races, deterministic clocks,
normal result routing, diagnostics, processing behavior, and source identity
before removing typed compatibility contracts. Audit Sources in its own
subsequent bounded pass.
