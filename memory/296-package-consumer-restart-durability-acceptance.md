# Package-Consumer Process-Restart Durability Acceptance

Date: 2026-08-07

## Result

The existing package-only behavioral gate now proves that FluxFlow's optional
SQL-file durability features compose correctly across a real process boundary.
One seed process persists and leases durable input/output work and exits without
settlement. A distinct recovery process opens the same files, starts the normal
Generic Host and FluxFlow hosted services, recovers both expired leases,
executes the workflow, captures its transformed output, and reaches terminal
input/output states.

No runtime assembly, public API, provider schema, package identity, package
version, publication state, or CI step was changed. The round extends the
existing external fixture, existing acceptance runner, release-governance
tests, documentation, goal records, and memory only.

## Implementation

- `eng/package-consumer-acceptance/Program.cs` now has one direct argument
  switch for the existing default behavior plus
  `durability-restart-seed <absolute-directory>` and
  `durability-restart-recover <absolute-directory>`.
- `RestartDurabilityScenario.cs` owns the complete bounded restart slice:
  deterministic envelopes and UTC times, SQL-file store registration, one
  explicit uppercase component/workflow, source-generated string JSON,
  Generic Host setup, durable-input dispatch, durable-output capture and
  delivery, terminal status assertions, and exact evidence markers.
- Seed mode enqueues one input and one independent output, acquires attempt-1
  leases with exact owners/times/non-empty tokens, applies one destination
  effect, and intentionally leaves both leases unsettled.
- Recovery mode observes both leases as expired at a fixed later UTC value.
  The registered `TimeProvider` fixes lease decisions while its base timers
  continue to run the real bounded hosted-service polling loops.
- The fixture-local destination derives its identity from
  `DurableOutputEnvelope.Key`. One atomically created `.effect` file contains
  the exact address, message identity, contract, and JSON payload and is both
  destination effect and idempotency receipt. Equivalent redelivery succeeds
  without rewriting the effect; identity/content conflict fails closed.
- Final persisted state is exactly one delivered input and two completed
  outputs, with no pending, leased, unmaterialized, or dead-lettered work. The
  two destination effects are the pre-applied seed effect once and the
  transformed workflow effect once.
- The external fixture now references the same four top-level FluxFlow
  candidates plus `Microsoft.Extensions.Hosting` 8.0.1. It remains `net8.0`,
  package-only, outside the solution, and free of project references.
- `eng/package-consumer-acceptance.ps1` copies all top-level fixture C# files,
  builds once, and launches default, seed, and recovery as three distinct
  processes. Seed and recovery receive the same runner-owned restart path.
  Exact per-phase markers are required once; missing or duplicate evidence
  fails closed. Existing restore isolation, candidate SHA-256 verification,
  caller/runner ownership, diagnostics retention, and cleanup remain intact.
- Normal CI still invokes the same acceptance runner once after solution
  tests. No second runner, project, workflow step, process framework, provider,
  or reusable runtime abstraction was added.

## Test Evidence

Focused `PackageConsumerAcceptanceScriptTests` passed 15/15 in Release with
zero warnings. Exact added or strengthened contracts include:

- `Acceptance_fixture_is_a_net8_package_only_consumer`;
- `Acceptance_restart_fixture_uses_explicit_hosted_recovery_and_idempotent_receipts`;
- `Acceptance_script_prepare_only_resolves_exact_closure_without_mutation`;
- `Acceptance_script_restores_verifies_builds_and_runs_from_retained_isolated_workdir`;
- `Acceptance_script_stops_before_recovery_when_seed_marker_is_missing`;
- `Acceptance_script_cleans_owned_workdir_after_invalid_recovery_marker_count`;
- `Acceptance_script_pack_mode_cleans_owned_source_and_workdir_after_success`;
  and
- `Acceptance_gate_is_part_of_the_complete_ci_rehearsal`.

The post-generation assertion and pseudo-mutation review added duplicate-marker
rejection, CI ordering, and explicit missing/relative/extra/unknown command-line
guard coverage. It found no assertion-free, trivial-only, self-referential,
skipped, timing-dependent, or network-dependent test. The mandatory static
test inventory analyzer ran once, but its result payload was lost when an
adjacent read-only inspection command failed to parse; no coverage claim relies
on that lost payload and the analyzer was not rerun.

## End-To-End And Repository Validation

- The final real `eng/package-consumer-acceptance.ps1 -PackPackages` run packed
  and hash-verified the exact nine-package candidate closure, restored into an
  isolated package cache, built the external `net8.0` consumer with zero errors
  and zero warnings, and passed every existing and restart marker.
- The seed and recovery command evidence shows two separate `dotnet run`
  processes over one absolute `restart-durability` directory. The final output
  included input recovery, workflow output capture, pending-output resumption,
  output recovery, idempotency, restart, runner restart-completion, and overall
  completion markers.
- Runner-owned candidate, consumer, package-cache, SQL-file, and effect paths
  were removed by the existing `finally` ownership boundary.
- Complete `FluxFlow.Release.Tests`: 166 passed, zero warnings.
- Complete Release build: 134 projects, zero errors, zero warnings.
- Complete Release solution tests: 2,534 passed across 66 projects, zero
  warnings.
- Solution `dotnet format --verify-no-changes` passed.
- Folder-mode whitespace verification for the package-consumer C# fixture
  passed.
- Full transitive vulnerable-package audit reported no vulnerable package for
  every solution project.
- `git diff --check`, the focused hygiene scan, and scoped formatting passed.

## Guarantee Boundary

This gate proves persisted lease recovery and normal hosted-service composition
for the local SQL-file providers. It does not make the workflow itself durable.
Engine queues, links, active revisions, node state, resources, checkpoints,
business transactions, broker acknowledgements, and arbitrary external effects
remain outside the persisted boundary.

Input and output delivery remain at-least-once. Provider-level suites continue
to own exact fencing-token, retry-attempt, compare-and-set, concurrency, schema,
and corruption proofs. The process-restart fixture proves the abandoned work is
recovered and settled, but does not expose internal lease tokens through a new
public or test-only seam merely to repeat those provider assertions.

The local effect file demonstrates the host's idempotency responsibility
without hiding a second receipt/effect transaction: the atomic idempotent
destination operation is the receipt. Real destinations must provide their own
equivalent idempotent operation or transaction. FluxFlow does not coordinate
that destination operation with delivery-store completion and does not claim
exactly-once behavior.
