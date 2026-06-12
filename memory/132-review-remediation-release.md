# Review Remediation Release

Date: 2026-06-12

## Decision

Fix all findings from the full code review (`131-full-code-review.md`) plus one
user-requested engine change, in a single hardening wave: engine `1.1.0` and 24
component package minor releases.

## Engine 1.1.0

- Node error channels are broadcast fanout sources (same `FlowFanoutSource`
  used by diagnostics): every linked consumer receives every error; unobserved
  errors are discarded instead of buffered without bound. This was a direct
  user requirement ("error output ... like the normal output block").
- Unlinked error output ports are drained by the runtime (the FlowError
  exclusion in `DrainWhenUnlinked` is removed).
- New central error streams: `RuntimeFlowError`, `FlowErrorCollector`,
  `ApplicationRuntime.Errors`, `Workflow.Errors`, `FlowApplicationHost.Errors`.
- Output ports detach a rejecting link instead of faulting siblings; new
  `OutputPort.LinkFailed` event; the runtime turns link failures into
  `flow.link.target.rejected` / `flow.link.condition.failed` diagnostics, and
  condition failures into `DynamicExpressionFailed` (3000) errors.
- Throwing conditional-link predicates drop the message for that link only.
- Upstream faults propagate to linked targets (no downgrade to completion);
  pump faults are unwrapped from `AggregateException`.
- Validation: rejects names containing `.`, the reserved `$resources`
  workflow name, and cyclic link paths (codes 12-15).
- Cancellation completes nodes as stopped instead of faulted; `StartAsync`
  rejects re-entrant starts; factory registry is thread-safe; dispose guards
  use interlocked flags; configuration scalar coercion is round-trip-lossless.

## Component Waves

Versions: Expressions/Journal/Secrets/Resources/Projections/Expectations/
Storage.FileSystem/Storage.SqlFile `1.0.0 -> 1.1.0`; Control/Mapping/
Assertions/Timers/Sources/State/Routing/Http/FileSystem/Mqtt/Serialization/
Payloads/Validation/Observability/Metrics/Sessions `1.1.0 -> 1.2.0`. Storage,
Designer, Configuration unchanged.

Highlights per area (full details in CHANGELOG.md):

- Errors output ports registered on Control, Mapping, Timers, Observability
  nodes (Assertions/Sources/State/etc. already had them).
- Routing: proactive correlation timeouts via the package clock; arrival-
  ordered expiry; race-free timer cancellation; dispose tolerates faults;
  design metadata corrected (switch Matched/Routed, correlation
  Request/Response, 30000 ms defaults).
- Security: Http `allowedHosts` + `restrictToBaseUrlOrigin` + header CR/LF
  rejection; FileSystem implicit-cwd path containment by default and 16 MiB
  default read cap; Secrets redactor fragment expansion.
- Storage adapters: FileSystem store shared per root (cross-node optimistic
  concurrency now serialized), collection-scoped queries, corrupt-file skip;
  SqlFile millisecond-truncated put timestamps, SQL-pushed prefix/paging,
  pool clearing on dispose; expired records no longer block Create in both.
- Timers: cron DST gap handling, vixie `value/step`, constant-offset delay,
  prompt fault-tolerant dispose. Mqtt: publish timeout + drain-then-dispose.
- Designer metadata: Sessions rewritten to match real nodes; Projections and
  Expectations gained providers (+ Designer dependency) and joined the
  coverage test.
- Release tooling: workflow expression injection eliminated via `env:`
  indirection; changelog gate enforced even with `-SkipSolutionBuild`; tags
  pin the pre-dry-run SHA; bootstrap script propagates native failures;
  obfuscated string-split literals replaced; dry-run restores from nuget.org
  by default and cleans stale packs; changelog sections must be non-empty.

## Deliberately Not Changed

- Expectations/Sessions error-code collisions (12001/12002) and Http/Timers
  (9000): changing released public constants is breaking; documented instead.
- Expression-support glue duplication across five packages: moving it into
  `FluxFlow.Components.Expressions` would change public options surfaces and
  add dependencies (Timers/Sources); deferred to a future major.
- `Type.GetType` config fallback kept (compat); cache writes are now
  thread-safe.
- No `Directory.Build.props` / `nuget.config` consolidation: packaging
  conventions are policed by release tests; deferred.

## Verification

- `dotnet build FluxFlow.sln --configuration Release`: 0 warnings, 0 errors.
- `dotnet test FluxFlow.sln --configuration Release --no-build`: 30 test
  assemblies, 684 tests, 0 failures (was 595 tests; 89 added/extended).
- Release guard tests pass with the new versions and changelog sections.

## Release Result

The release is complete. All 25 packages (engine `1.1.0` + 24 component
minors) were tagged via the guarded tag helper in dependency-wave order
(engine -> engine-only dependents -> Expressions/Projections dependents),
published by CI, and verified on the public feed:

- PR #2 merged the remediation work; PR #3 fixed a release-tooling gap found
  during the engine dry run (the guarded tag script's empty
  `AdditionalPackageSources` default overrode the dry run's nuget.org
  default, so local feed-style verification could not restore external
  transitive dependencies).
- All 25 publish workflow runs concluded successfully. Two runs needed a
  rerun for transient causes, with no bad artifacts published: the Metrics
  run hit the flaky `Expect_TimesOutWhenMatchIsNotObserved` Expectations
  timing test at the Test step (before any push), and the Expressions and
  State runs pushed successfully but exceeded the 20-attempt post-publish
  feed verification window because of nuget.org indexing lag.
- Public feed lookup confirmed all 25 new versions visible; the package list
  helper reports all 28 aliases consistent with tags and project versions.

Follow-up candidate for the next maintenance pass: stabilize the flaky
Expectations timeout test and consider a longer post-publish verification
window for nuget.org indexing lag.
