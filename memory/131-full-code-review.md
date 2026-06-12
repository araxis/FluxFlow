# Full Code Review

Date: 2026-06-12

## Scope

Read-only review of the entire solution at the component-stable baseline
(engine `1.0.1`, components `1.0.0`/`1.1.0`): `FluxFlow.Engine`, all 27
component packages, `eng/` release scripts, GitHub workflows, and release
tests. Seven parallel review passes covering security, performance,
correctness, concurrency, and maintainability. No code was changed.

## Top Findings (High)

1. Engine, `Runtime/OutputPort.cs:282` — when any linked input declines a
   message (target faulted/completed), the pump faults all sibling targets and
   detaches the producer silently; nothing observes the port completion in the
   single-source case, the runtime stays `Running`, and the producer's buffer
   grows unboundedly. One dead downstream node stalls a workflow invisibly.
2. Engine, `Mapping/ExpressionFlowPredicate.cs:27` + `OutputPort.cs:277` — a
   conditional-link expression that throws at runtime permanently faults the
   whole output port and all propagating targets.
   `FlowErrorCodes.DynamicExpressionFailed` (3000) exists but is never used.
   Per-message drop + diagnostic is the intended shape.
3. Engine-wide error channel, `Core/FlowNodeBase.cs:7` + `OutputPort.cs:83` —
   `_errors` is an unbounded `BufferBlock<FlowError>`; unlinked error ports are
   excluded from drain-when-unlinked; nothing engine-side consumes errors.
   Worse, Control (`flow.filter`/`flow.when`), Mapping (`flow.mapper`), the
   Timer transforms (delay/throttle/debounce), and all three Observability
   nodes never register an `Errors` output port at all, so flows cannot link
   it. A sustained per-message failure (routine misconfiguration) is both
   invisible and an eventual OOM. Fix in engine (bound/drain) plus register
   missing `Errors` ports in those packages.
4. Http, `Nodes/HttpRequestNode.cs:281-335` — SSRF surface: an absolute
   per-message URL bypasses `BaseUrl`, there is no scheme/host allowlist, and
   `DefaultHeaders` (typically credentials) are attached to every destination.
   Add allowlist/same-origin options and README guidance.
5. FileSystem, `Options/FileSystemPathResolver.cs:26-29` — with the default
   config (no `baseDirectory`) relative per-message paths like `..\..\x`
   resolve anywhere on the volume; `AllowAbsolutePaths=false` only blocks
   rooted paths, giving false containment. Containment is also lexical only
   (symlinks/junctions escape).
6. Storage.FileSystem, `FileSystemStorageStore.cs` — the only concurrency
   guard is a per-instance lock, but the factory creates a new store instance
   per node lease, so `ExpectedVersion` optimistic concurrency and
   `Create` mode are unserialized across nodes/processes: lost updates, both
   creates succeeding, sporadic Windows IO errors. SqlFile adapter is immune
   (immediate transactions), so adapters silently differ in safety.
7. Routing, `Nodes/FlowCorrelationNode.cs:206` — correlation timeouts are
   lazy-only (evaluated on next message arrival or completion); an idle stream
   never emits `Timeouts` and pins pending payloads. The join node already has
   the correct versioned proactive timer pattern to port.

## Medium Findings (grouped)

- Engine: upstream faults are downgraded to normal completion downstream
  (`OutputPort.cs:205-224`); validation misses name invariants (`.` in
  node/workflow names throws from `Build` instead of failing validation) and
  cycle detection (cyclic workflows have no entry nodes, `Complete()` becomes
  a no-op, graceful stop impossible).
- Storage.SqlFile: `StoredAt` returned at full precision but persisted at ms
  (returned record never equals re-read; `StoredFrom = putResult.StoredAt`
  excludes the record); expired records block `Create` mode (Get and Put
  disagree about existence — both adapters); query paging not pushed to SQL
  (full collection materialized per query); connection pooling keeps the db
  file open after dispose.
- Storage.FileSystem: query scans every collection under the root with
  AllDirectories instead of the collection subdirectory; one corrupt json file
  permanently fails all queries.
- Timers: cron DST handling skips spring-forward occurrences and stalls during
  fall-back overlap (`CronSchedule.cs:151`); `timer.delay` applies cumulative
  (serialized) delay, not arrival+delay; transform dispose awaits faulted
  `Completion` unguarded and can block for queued×interval.
- State/Metrics: `_rejectedKeys`/`_rejectedGroups` grow unboundedly under
  high-cardinality keys (defeats the max-keys/groups caps); reducer emits
  state aliased to internal state and shares one initial-state instance across
  keys; metrics builds a full snapshot per sample even when not emitting.
- Journal: retention options exist but nothing invokes `PruneAsync`; O(n)
  duplicate-id scan per append (O(n²) sustained).
- Mqtt: `PublishAsync` has no cancellation token or timeout — a hung adapter
  wedges the node and dispose; publish dispose path drops queued messages
  (no drain-then-dispose like Http).
- FileSystem: `DirectoryEnumerateNode.StartAsync` can run the whole
  enumeration synchronously under the state lock; enumerate follows reparse
  points (symlink cycles); watch buffer size not configurable.
- Designer metadata drift: Sessions query metadata declares wrong port/type
  and nonexistent options; Routing omits split correlation inputs and
  advertises wrong timeout default; Observability shares one wrong option
  list across three nodes; Projections/Expectations ship no providers and the
  coverage test omits them, hiding the gap.
- Release tooling: workflow-expression injection into `pwsh run:` blocks in
  `publish-nuget.yml`/`package-maintenance.yml` (pass via `env:` instead);
  `package-release-tag.ps1 -SkipSolutionBuild -Push` bypasses the changelog
  gate; dry-run feed verify cannot restore external deps with default args;
  dependency-wave ordering is operator-discipline only; actions pinned to
  tags not SHAs; no repo `nuget.config`/source mapping/lock files.

## Cross-Cutting Patterns

- Dead cancellation plumbing: `_processingCancellationToken =
  inputOptions.CancellationToken` is always `None` in ~12 nodes across
  Routing, Control, Mapping, Assertions, State, Serialization, Validation,
  Projections, Metrics, Observability — every related catch filter is
  unreachable. Wire a real lifecycle CTS (join/window/Sessions pattern) or
  delete.
- `ResolveType` falls back to `Type.GetType` on config strings and caches into
  an unsynchronized shared dictionary — duplicated in ~6 packages; gate behind
  opt-in and use `ConcurrentDictionary`.
- ~100 lines of expression-support glue (type alias map, context factory)
  copy-pasted across five packages; belongs in `FluxFlow.Components.Expressions`.
- Ignored `SendOutputAsync`/`Post` results after `Complete()` races: Timers
  interval, MQTT subscribe, DirectoryEnumerate, SessionReplay silently drop
  while diagnostics claim success.
- Clock hardening is essentially complete; stragglers:
  `FlowAssertionResult.EvaluatedAt` and `MetricSnapshotOutput.Timestamp`
  default to `DateTimeOffset.UtcNow`; engine diagnostics timestamps.
- Error-code band collisions: Expectations vs Sessions (12001/12002), Http vs
  Timers (9000).
- `"g" + "it"`-style string-splitting obfuscation in
  `package-release-tag.ps1`/`publish-nuget.yml`/tests defeats grep audits —
  use plain literals.

## Biggest Test Gaps

- Engine failure paths: link rejection, conditional-predicate throw, upstream
  fault propagation, cycles, unlinked error ports, fanout backpressure.
- Correlation timeout without traffic (would fail today); duplicate-side
  semantics; join/window dispose-after-fault.
- Cron DST/timezone behavior (all existing tests are UTC).
- Secrets: no test pins the core guarantee (ToString/serialization never leak
  values).
- Per-adapter storage: concurrency, multi-page stability, Put/re-read
  precision round-trip, expired-record Create.
- FileSystem traversal with default (no baseDirectory) config; symlinks.
- Release scripts: tag guards and execute paths untested; changelog test
  format drifts from `get-release-notes.ps1`.

## What Held Up Well

- Engine build pipeline (result-based errors, partial-graph cleanup, port type
  enforcement, multi-source completion links, dispose robustness) and safe
  JSON handling.
- Single-threaded ActionBlock state confinement across all stateful nodes;
  versioned timers in join/window; per-message failure isolation everywhere.
- SqlFile parameterization (no injection), FS adapter SHA-256 path hashing (no
  traversal), Http resource handling, MQTT topic validation, secrets
  containment design, release script input allowlisting and `&`-array
  invocation (no Invoke-Expression).

## Recommended Priority

1. Engine error-channel + fault-propagation wave (findings 1–3, fault
   downgrade, validation gaps) — engine `1.1.0`.
2. Security hardening: Http allowlist, FileSystem default containment,
   workflow `env:` indirection — patch/minor releases.
3. Storage.FileSystem concurrency + SqlFile timestamp/expiry semantics.
4. Correlation proactive timeout; timer/cron fixes.
5. Designer metadata corrections + Projections/Expectations providers.
6. Cross-cutting cleanup (dead cancellation, ResolveType, expression glue,
   error-code bands) opportunistically.
