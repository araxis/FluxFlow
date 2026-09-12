# Timer burst verification hang

## Evidence and cause

The revision queue audit's full regression did not complete. Its timer testhost
(PID 119324, parent 38336) remained live. Managed thread stacks showed passive waits,
not the initially suspected debounce lock deadlock. A heap dump identified the actual
test: TimerDelayNodeTests.Delay_burst_shares_one_constant_offset_from_arrival.

The async state machine retained two received messages and a TimeoutException while
awaiting FlowProcessingLifetime.DisposeAsync. A TimerDelayNode.EmitWhenDueAsync task
still awaited a fake-clock delay. Dump field inspection showed:

| Field | Ticks | Relative to test start |
| --- | ---: | ---: |
| Fake clock now | 639159984000400000 | 40 ms |
| Remaining pending item's due time | 639159984000800000 | 80 ms |

The test admitted three messages, waited for the first timer registration, then
advanced virtual time by 40 ms. Admission did not establish that all three messages
had been processed. The third message could receive its due time after that advance.
After its receive timed out, await-using tried to drain gracefully, but virtual time
could never advance again. This explains both the initial timeout and the indefinite
test-suite hang. No production delay bug or monitor deadlock was established.

Evidence: artifacts/core-refactoring/revision-queue-audit/timer-async.txt,
timer-state.txt, timer-cause.txt, timer-deadlines.txt and timer-ticks.txt, alongside
the preserved heap dump and original aborted regression reports. After collecting
evidence, only the exact verified testhost was terminated. Session 99756 then exited
with regression failure code 1. It is no longer an active run. Its partial results
must never be presented as a successful full regression.

## Change

The existing test still sends a three-message burst with one constant offset.
It now completes the input and awaits native Input.Completion before advancing time,
establishing that every due time was calculated against the same clock value. Waiting
for the first timer additionally ensures its virtual delay is armed. This uses the
real native processing barrier, with no sleep or production/test-only runtime hook.

The original value/order assertions remain. New assertions verify no node completion
before time advances and no active fake timer after final drain. On an escaping test
failure, the test faults its node before await-using disposal; pending virtual delays
are canceled and the original error can reach the test runner. No production code,
delay semantics, shutdown policy, packages or public APIs changed.

## Verification

The exact corrected test passes once after rebuild, then 20 times in separate fresh
test processes. The complete timer suite also passes (77 cases). Fresh full solution
verification passes: nonincremental Release build with zero warnings/errors, 4,409
local regression cases in 79 reports, and 4,619 discovered cases including all four
new native prepared-output cases. All 1,481 inventoried source inputs remained
unchanged during verification. Compared with the prior queue-audit snapshot, only
TimerDelayNodeTests.cs changed among these inputs. Exact commands/results are in
artifacts/core-refactoring/timer-burst-verification/verification.json.
The new run uses bounded hang
diagnostics so a future asynchronous cleanup defect does not silently stall forever.
External/container suites and performance are not covered by this run.

## Focused test-quality review

Reopened the modified method. Four explicit assertion sites cover negative output,
incomplete node state, exact ordered values and zero active timers. Successful
bounded awaits additionally exercise input processing, each delivery and final drain.
The method is neither assertion-free nor trivial-only, and preserves its nontrivial
three-message burst. No assertions were weakened or skipped.

Static mutation reasoning: emitting immediately or flushing delays early conflicts
with preadvance assertions; delaying each queued item by a fresh full interval would
time out on later receives; dropping/reordering/changing values conflicts with the
exact sequence. Removing the new processing barrier reintroduces the observed
scheduling race, but an individual run might still pass. The new failure-cleanup
catch is source-reviewed, not independently fault-injected. No mutation kill rate or
coverage percentage is claimed. This test correction unblocks qualification; it does
not establish completion of the broader refactoring goal.
