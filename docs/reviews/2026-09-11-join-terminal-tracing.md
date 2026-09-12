# Join blocked-output terminal tracing checkpoint

The completion-time join flush now uses the existing lifetime cancellation token.
Graceful input completion leaves this token active and drains accepted output. Forced
fault cancels a blocked flush while preserving the exact original owner exception.
This is one changed argument, with no new runtime, state, API or dependency.

## Requirement evidence

| Requirement | Evidence |
|---|---|
| R1: operation duration covers admission, not downstream delivery | `Blocked_join_emission_span_settles_with_admission_not_downstream` holds a native target, fills bounded output, and observes a third live scope. Releasing one delivery settles the third scope while remaining output and node completion are still pending. All three exact values, order, lineage and derived carrier are checked. |
| R2: terminal outcome and cleanup | The same test covers match/timeout with graceful drain, owner fault and declined admission. It verifies cancellation versus failure, exact owner-fault identity at node and target, no unaccepted delivery, stopped scopes and caller-context restoration. |

Six scenarios run on both supported frameworks, using existing postponed-target and
fake-clock helpers without sleep-based synchronization or production testing hooks.
The initial run failed timeout/owner-fault on both frameworks: completion flush used
CancellationToken.None and therefore recorded admission failure instead of cancellation.
The existing stopping token fixes that inconsistency. Initial failed reports remain
available alongside passing evidence.

## Verification

- Wider Join filter after the production fix: 130 passes across both frameworks.
- After strengthening fault assertions: 12 focused passes and 36 more executions in
  three fresh-process repetitions.
- Fresh nonincremental Release solution build: zero warnings/errors.
- Selected local regression: 4,641 passes in 80 reports, zero failures/skips.
- Solution discovery: 4,851 including all 12 additions.
- Stable qualification inputs: 1,483; all recorded commands exited successfully.

Authoritative commands, logs, TRX files and snapshots are under
artifacts/core-refactoring/join-terminal-tracing, with verification.json recording
the completed checks. The 33-assertion review and eight selected pseudo-mutations are
in .testagent/join-terminal-tracing-review.md. Only the lifetime-token red/green was
executed as a fault demonstration; the other mutation checks are static reasoning,
not a measured mutation score. The testing skill drove deterministic lifecycle
checks and exact fault assertions. The telemetry skill kept scope ownership at the
library operation boundary without introducing host/exporter infrastructure.

## Remaining scope

The regression filter explicitly excludes acceptance, container, external smoke and
database integration suites. This checkpoint covers the specified full-recording
held-output paths, not all interleavings, sampling modes under saturation, non-message
command errors or callback discovery/latency. Bounded history aggregation and remaining
source/custom/transport/durable/revision context require separate qualification.

Enabled/off runtime load, retained memory and a refreshed coherent local package
candidate remain required. Prior performance and package evidence predates current
production changes. The full goal remains active; nothing was published or sent to
external consumers.
