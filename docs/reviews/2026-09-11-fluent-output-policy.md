# Explicit direct Fluent output policy

## Decision and implementation

The prior Build implementation discarded every output without a fluent link.
An accidentally omitted source, continuation or branch therefore appeared to
finish successfully. This contradicted the explicit-discard Core contract.

FlowGraphBuilder now leaves those outputs strict and removes its redundant
linked-output set. FlowBuilder exposes DiscardUnmatched for the current endpoint.
To/Tap explicitly mark their terminal component's declared business outputs for
unmatched discard. Existing links still receive messages; input-only terminals
need no artificial output. Routing, backpressure and lifetime remain Core-owned.

Migration is intentional and breaking within the unpublished preview: replace
an intentionally terminal Then(node).Build() with To(node).Build(), or explicitly
select DiscardUnmatched on the intended continuation/branch. An unused declared
port that never emits does not fault. All builder mutations remain forbidden
after Build. No context-aware Add overload, component creator or new runtime is
introduced.

## Verification

The original three regression cases compiled and failed before production edits:
unlinked source, unlinked processing continuation and an omitted odd branch. Each
failed because graph completion did not throw, establishing the behavioral defect.

| Gate | Result |
| --- | --- |
| Full nonincremental Release solution build | Zero warnings/errors |
| Selected local regression | 4,839 passed, zero failed/skipped; 81 reports |
| New policy cases | All 11 passed; Fluent suite totals 39 |
| Solution discovery | 5,049 cases; includes all 11 new cases |
| Existing Fluent sample | Successful linear and branched execution |
| Qualification source inventory | 1,486 inputs unchanged throughout the run |

Evidence lives in artifacts/core-refactoring/fluent-output-policy: red/red.trx,
regression-trx, build.log, discovery.log, sample.log and verification.json. The
initial focused green report has 38 cases; the final full run includes the last
terminal-side regression and therefore has 39 Fluent cases. This is not an
inconsistency or a second full-framework count.

The test workflow used a scoped static pairing analysis and concrete assertion
and hypothetical-mutation review. Exact method-to-requirement mapping is in
.testagent/fluent-output-policy-review.md. No runtime coverage or mutation score
is claimed. The public API baseline was intentionally accepted and subsequently
verified by the normal full regression, without the acceptance variable.

## Remaining scope

The selected regression filter excludes Acceptance, ContainerIntegration,
ExternalSmoke and the dedicated remote relational integration suite. Discovery
is not evidence those excluded cases ran. The new unit test file targets net10.0;
the solution's existing multi-targeted projects also ran on their declared targets.

The frozen t1400 local feed predates this fix and retains its historical evidence;
it was not overwritten. A fresh candidate and package-only consumer run remain
necessary. Broader capability, provider, telemetry and performance qualification
remain separate open gates. Nothing was published and no external consumer was
contacted. The overall refactoring goal remains active.
