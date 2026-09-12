# Candidate startup transaction

## Finding and fix

The optional update host could reject a revision after its source failed to start,
while stable input attachments and the port revision already pointed to that failed
candidate. The application snapshot still named the old revision. Disposing the
failed candidate detached its ports but did not restore the previous input targets.

Two compiled regression cases reproduced the mismatch before production changes:
source startup throwing and source startup canceled by the caller. Evidence is
retained under artifacts/core-refactoring/activation-rollback-20260911/red.

Source startup now runs within the existing precommit boundary:

1. Acquire the existing affected-port revision gates and validate the candidate.
2. Start candidate sources against the already-created bounded output staging.
3. Recheck cancellation, output faults and route validity before attaching outputs,
   replacing input targets and publishing the new routing/revision.

On activation failure, the candidate lifetime faults its components before normal
candidate disposal. This also handles cancellation requested by a source that then
returns normally. No alternate rollback runtime or public lifecycle abstraction was
introduced. Changes are confined to three internal Engine files, a net increase of
17 source lines; Core, dependencies, queue capacities and exported APIs are unchanged.
This is a correctness repair, not a throughput optimization.

## Verification

Five new cases in ApplicationActivationRollbackTests cover:

- Failed_source_start_preserves_active_revision_and_routing: failure/cancellation,
  unchanged application snapshot and port revision, actual request/reply through the
  original component before and after rejection, and once-only candidate cleanup.
- Startup_output_is_published_only_after_successful_commit: success, exception and
  cancellation after a normal startup return. The test holds startup causally,
  checks no commit, verifies ordered payload/trace identity on success, and verifies
  a completed empty observation after rejection.

The existing
Fallback_changes_are_revision_changes_and_conflicts_do_not_replace_live_routes
test additionally verifies that a conflicting route never invokes source startup.
All six focused cases pass, as do all 531 Engine cases. Assertion and reasoned
mutation review is recorded in .testagent/activation-rollback-review.md; no line
coverage percentage or executed mutation score is claimed.

The initial full qualification had a clean build but one source-declaration baseline
failure. That scanner includes public members of internal types. Review confirmed
only Engine's hash changed, retaining 387 declarations, for the internal precommit
parameter. The single baseline entry was updated; both baseline tests pass.
The failed run is frozen under qualification with a disposition. The fresh complete
run and its machine-readable outcome are under qualification-final:

- Full nonincremental Release build: zero warnings/errors.
- Selected local regression: 4,848 passed across 81 reports, zero failed/skipped.
- Discovery: 5,058 cases, including all five new startup cases.
- Fluent sample: passed.
- All 1,488 source/configuration qualification inputs stable throughout the run.

All four commands exited zero. TRX totals were independently summed after the
verifier finished. Acceptance, container, external smoke and TSql integration
categories remain excluded by the recorded local filter; discovery is not execution.

## Scope and remaining work

The existing local resource.2 candidate is frozen and does not contain this repair.
Do not publish it as the corrected candidate or silently reuse its version for new
bytes. A fresh coherent local package build and consumer qualification are still
required. Actual revision changes with traffic in flight remain a separate load
qualification task; these deterministic startup cases do not replace it.

The fix protects routing on source-start failure. It does not undo external effects
already performed by startup, prove arbitrary postcommit failures recoverable, or
newly qualify different-port-generation adoption failures. Source StartAsync must
initiate production, not wait for the whole workflow to finish: precommit staging
is intentionally bounded. No new performance SLO is assumed; the target remains
ordinary applications with simple composition, reliable delivery, bounded resources
and measured responsiveness.

No packages were published, no external consumer task was contacted, and no feature
or test was removed. The overall refactoring goal remains active.
