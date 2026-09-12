# Event dictionary capacity: workflow measurement

The four capacity initializers in FlowEvent.cs now have matched whole-workflow
evidence, in addition to the 552-byte event-encoding allocation probe. The comparison
finished with exit code zero in session 42040; no process remains live.

## Scope and provenance

Both variants use the same current benchmark executable and dependencies. Only
Core DLL/PDB differs, taken from the previously qualified before/after probe
artifacts. Engine and every other runtime file are hash-identical between variants;
Core and Engine symbol identities match their assemblies. The source comparison
identifies only FlowEvent.cs as changed. No additional production edits this turn.

Sixteen 256-message smoke cases exercise direct and revision-enabled small/deep
workflows on both frameworks. Thirty-two timed cases cover the changed hot path:
revision-enabled workflows, 1 hop/1 target and 32 hops/8 targets, four alternating
before/after pairs on net8 and net10. Each timed run uses a fresh process, capacity
16, four producers, a shared 64-KiB JSON value, 64 warmup messages and ten seconds
of production. This is not a new direct-runtime throughput comparison.

Every run passes exact per-target/admitted/completed counts, no reliable rejection,
bounded tracking, expected latency sample counts, payload identity and zero pending
business work/queues after drain. Final verification confirms 1,481 stable source
inputs and 244 stable runtime files.

## Results

Values below are medians of four paired differences. Positive throughput is faster;
negative allocation and latency are lower. Each allocation pair is lower: 16/16.

| Framework | Hops / targets | Throughput | Range across pairs | Allocation bytes/completed | Completion p99 upper bound |
|---|---|---:|---|---:|---:|
| net8 | 1 / 1 | +1.13% | -0.32% to +3.38% | -1,663.98 | -8.30% |
| net8 | 32 / 8 | +4.37% | +2.69% to +7.83% | -41,872.09 | -6.20% |
| net10 | 1 / 1 | +5.77% | +2.85% to +31.59% | -1,693.90 | -17.26% |
| net10 | 32 / 8 | +5.14% | -0.21% to +11.66% | -42,056.50 | -2.84% |

The net10 small-workflow +31.59% pair is a large timing outlier; it is not a credible
general speed claim. Medians are descriptive, not confidence intervals. Deep net8
is faster in all four pairs; deep net10 in three of four. CPU medians are not uniformly
lower across every case. The consistent allocation reduction is the stronger result.

The improvement does not erase the remaining optional-host overhead: the net10 deep
after variant still allocates roughly 549 KB per completed message in this workload.
Do not remove event, revision or routing features to conceal that cost. The previously
identified immediate diagnostic encode/decode work remains a separate candidate for
simplification with explicit external-validation and observer-ordering tests.

## Evidence and remaining gates

Artifacts: `artifacts/core-refactoring/event-capacity/load/results/verification.json`,
`comparison.json`, `runs.json`, `timed-details.json`, `descriptive-details.json`, all
individual reports/logs and binary/source inventories. Supporting scripts preserve
existing outputs and refuse replacement runs over them.

The last changed-source build/regression remains the prior checkpoint: clean full
Release build, 4,409 local passes and 4,619 discovered cases. This turn changes only
measurement scripts/documentation; source hashes remained stable during measurement.
No new tests, public API, packages or libraries were introduced.

This qualifies a measured benefit for the specified workload, not the complete
performance matrix. Allocation includes the harness, latency is a logarithmic bucket
upper bound, affinity/priority are uncontrolled and business drain is not full
observation quiescence. Other payloads, conditions, telemetry, cancellation/revision
load, long retention, remaining capability gates and final candidate refresh remain
open. The earlier local feed predates this correction. Nothing published and no
external consumer coordination performed.
