# Active-host revision retirement

Status: selected stable/changed-address retirement scope qualified without
production changes. This is bounded correctness/collectibility evidence, not
throughput, instantaneous unrooting or universal leak freedom.

## Qualified result

The package-only [fixture](../../artifacts/core-refactoring/active-revision-retirement-20260912/Program.cs)
passed four final smokes and eight fresh qualification processes: two repetitions
per address mode and target framework, with reversed order in the second round.
Each qualification process performs sixteen replacements and seventeen groups of
sixteen requests. Totals: **128 replacements, 2,176 exact replies, 136 components
constructed/disposed once, 64 removed-address rejections**. Both net8.0 and net10.0
pass. Every live-host and final collection checkpoint clears at the first attempt
after the explicit 20-ms scheduling interval.

| Requirement | Evidence |
| --- | --- |
| Retire replaced components while host remains useful | `Retired_revisions_are_collectible_while_host_is_active`: all previous weak probes clear after each applied update; current instance remains alive/not disposed and answers sixteen new requests |
| Keep stable public facade; handle changed physical addresses | Same case: facade reference unchanged, revision IDs exact, alternating removed inputs throw `KeyNotFoundException`, replies contain current revision ID |
| Exact exchange and lifetime accounting | Same case: expected values, shared business trace, independent non-null correlations, exact processing/activation/disposal counts, stop and completion awaited |
| Drain accepted public work | `Public_queues_drain`: nonempty port snapshot reaches zero pending within fifteen seconds |
| Avoid vacuous retention proof | `Replaced_components_are_collectible`: exact nonempty probe count, bounded eight scheduling-interval/full-collection attempts, all old probes checked again at every update and all seventeen after shutdown |

There are 128 distinct components checked after replacement while the host is
active; eight final active components are additionally checked after shutdown.
Repeated checks of earlier probes are not additional unique components: live
checks total 8 × (1 + … + 16) = 1,088, followed by 136 post-shutdown probe checks.

## Identity and reproducibility

[Verification record](../../artifacts/core-refactoring/active-revision-retirement-20260912/qualified/verification.json)
SHA-256: `2A421BD692ED78654036F5F3932E5C11D9260883097DF51DC861DBE574B654D1`.
The runner preserves outputs, validates every result, and hashes fixture inputs,
runtime files, package archives and consumed production assemblies.

Fresh nonincremental Release solution build: **zero warnings/errors**, 19.35 s.
Consumer build also clean. All **1,491** source inputs equal the HTTP-qualified
checkpoint before and after; all 36 runtime files and four fixture inputs remain
stable. Ten consumed DLLs from five local packages match both context.1 inventory
and current Release output across the two frameworks. This dependency closure
does not contain the newer HTTP correction; context.1 as a whole remains stale
for that correction and must not be represented as the final candidate.

No permanent solution tests were added. The previous 4,861-pass local regression
count remains historical/current-source-equivalent; it was not rerun in this batch.
The eight console qualification runs are not additional discovered solution tests.

## Fixture corrections and limits

The initial immediate queue snapshot failed because reply delivery and dispatcher
bookkeeping are separate observations; it was replaced by bounded eventual
drainage. Subsequent live-host checks passed, but immediate-yield collections
sometimes left the final component alive after shutdown. Explicitly clearing
fixture owner locals alone did not resolve that timing variability. Eight bounded
checks with 20-ms scheduling intervals passed the final batch. No production change
or heap-root diagnosis was made: the exact transient root is not identified.
The saved first failure and assertion review retain this limitation.

Factories construct new components rather than capturing instances in definition
closures. Tracking contains only weak references and standalone counters. See
[assertion review](../../.testagent/active-revision-retirement-review.md) for exact
oracles and reasoned mutations; no mutation engine or coverage percentage is claimed.

The workload is sequential between revisions, scalar strings, bounded ports and
no installed telemetry SDK. Prior held-work replacement, successful sustained load
and disposed-observer checks remain separate evidence. Remaining scope includes
optional-host stop/fault load, timed SDK overhead, final acceptance reconciliation,
available real-service integration and a coherent refreshed package candidate.
No publication or external consumer coordination occurred.
