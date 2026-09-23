# Component request/reply simplification — 2026-09-22

## Problem and final structure

The only production caller of the public generic PendingExchangeCoordinator was
FlowRequestReply, used by Retry and RateLimit. That caller needed one canonical
message reply, not arbitrary contexts/outcomes, completion DTOs, feedback categories,
default-timeout options, custom comparers or reusable FaultAll batches. In particular,
the public Reply method reduced late/duplicate/missing feedback to false, so the
settled-key history provided no distinction to component consumers.

The final path is Retry/RateLimit -> FlowRequestReply -> internal PendingRequests.
FlowRequestReply owns message correlation, tracing and cooperative send admission.
PendingRequests owns active waiters, deadlines and their synchronization. It returns
a task with a reply/error tuple, not another public result abstraction. A private
Request holds its deadline and asynchronous completion source. No new component-facing
concept or registration change is introduced.

Six production files were deleted and one internal file added. Coordination has
2 C# files and 249 physical lines, down from 7 files and 604 lines (355 fewer, 58.8%).
FlowRequestReply itself decreased from 99 to 86 lines. Counts include whitespace
and comments and are limited to this slice, not the accumulated branch diff.

## Preserved behavior

- Fresh correlation per request/attempt plus expected reply address; original payload,
  lineage, headers and existing tracing behavior remain.
- Only the first matching reply wins. Old, duplicate and unrelated replies return false.
- Capacity includes infinite requests. Reply, timeout, cancellation and stop release it.
- The timeout covers admission and waiting for a reply. Even a delayed timer callback
  cannot allow a reply at/after its deadline to win.
- One TimeProvider timer and a priority queue serve all deadlines, including long
  timeouts. Queue entries retain only keys, not completed reply payloads. Stale entries
  are compacted. Each correlation is freshly generated; there is no key-reuse API.
- Runtime state transitions are synchronized and continuations are asynchronous.
- Original send/stop errors and cancellation tokens propagate. Pending send admission
  is cancelled and awaited when the request ends; it must cooperate with cancellation.
- Timer scheduling failures drain waiters and close admission. Already settled outcomes
  remain settled. Disposal attempts timer cleanup even after a stop failure and reports
  timer/disposal errors. Stop and repeated disposal retain existing semantics.

## Intentional API removal

Removed PendingExchangeCoordinator, PendingExchangeCoordinatorOptions,
PendingExchangeCompletion, PendingExchangeStart, PendingExchangeFeedback and the three
PendingExchange status/kind enums. Direct external users of those public generic types
must migrate; this is an intentional development API break, not binary compatibility.
FlowRequestReply's constructor and RequestAsync/Reply/PendingCount/Stop/DisposeAsync
signatures are unchanged. Retry and RateLimit production files required no edits.

The generic context/result inspection, feedback classification, key reuse history and
FaultAll contracts were removed rather than renamed or moved into another package.
Historical migration guidance is explicitly marked; current Coordination documentation,
the changelog and source API baseline are updated.

## Verification

- Full solution Release run: **1,786 passed**, no failures. Core, Nodes, Configuration
  and component projects exercise .NET 8/10; Coordination and Release projects retain
  their existing .NET 10 target. No claim that every project is dual-targeted.
- Existing request/reply and tracing focused run: 31 tests per target passed.
- Coordination coverage migrated from generic tests to **27 public request/reply
  lifetime cases**. Deadline boundaries, capacity/infinite waits, timer compaction,
  concurrent reply/cancel/timeout, concurrent start/shutdown, one timer/long deadlines,
  failed scheduling, stop/disposal failures and asynchronous cleanup are covered.
  Generic-only contracts and duplicated coverage were removed/consolidated; this
  reduces the full execution count by 18 from 1,804 without dropping the feature.
- Public API baseline updated and passed, then checked again by the full suite.
- Isolated Core NuGet consumer passed on .NET 8 and .NET 10. Added an actual correlated
  request/reply round trip to that consumer, checking payload identity and released capacity.

Evidence in artifacts/cleanup-review-20260920:
request-reply-before.json, request-reply-focused.log, request-reply-lifetime.log,
request-reply-api.log, request-reply-tests.log and request-reply-core-package.log.
The initial lifetime log has 26 cases; the full solution log contains the final 27.

No commit or publication was performed. This closes the generic coordination cleanup;
it does not assert that every remaining Core abstraction is necessary or unnecessary.
