# Prepared-dispatch bookkeeping: measured comparison

Status: all workloads verified; performance acceptance remains open.

The comparison completed in process session 94278 with exit code zero. Sixteen
smoke runs and 64 timed runs passed. Source inputs (1,481) and runtime files (248)
remained unchanged throughout. Results live under
`artifacts/core-refactoring/bookkeeping-paired/qualified/results`.

## What changed and what this proves

The before runtime is the frozen notification-optimized checkpoint. The after
runtime replaces only Core and Engine with the current package-qualified DLL/PDB
pair. Core removes two unused ordinary-delivery volatile counter writes; prepared
dispatch retains its writes and Engine uses the renamed internal getter.
Other dependencies and the load executable are byte-identical between variants.

The initial preparation attempted to additionally require every dependency to
match the current full-package candidate. A rebuilt JSON DLL differed in hash,
although its source, assembly identity and product version were unchanged. That
failed preparation is preserved. This corrected experiment freezes dependencies
identically to isolate the Core/Engine change; it is not a measurement of the entire
current package assembly composition. No cause for the unrelated hash difference
is asserted.

Workload: shared 64-KiB JSON, four producers, capacity 16, 64 warmup messages,
ten-second timed runs, both supported frameworks. Each topology/runtime case uses
four before/after pairs with alternating order. The runner verifies exact target
counts, admitted/completed accounting, no reliable rejection, bounded tracking,
payload identity and zero pending business work/queues after drain.

## Paired observations

These are medians of four paired percentage differences, not confidence intervals.
Positive throughput is faster; positive allocation/latency is higher.

| Framework | Runtime | Hops / targets | Throughput | Slower pairs | Allocation bytes/completed | Completion p99 upper bound |
|---|---|---|---:|---:|---:|---:|
| net8 | Direct | 1 / 1 | +2.26% | 1/4 | +3.60 | +1.16% |
| net8 | Direct | 32 / 8 | -1.35% | 2/4 | -34.68 | -9.63% |
| net8 | Updates | 1 / 1 | +0.96% | 2/4 | -99.46 | +2.24% |
| net8 | Updates | 32 / 8 | -2.31% | 3/4 | +146.73 | +10.24% |
| net10 | Direct | 1 / 1 | +3.30% | 1/4 | -39.03 | -3.77% |
| net10 | Direct | 32 / 8 | +0.13% | 2/4 | -47.47 | -2.09% |
| net10 | Updates | 1 / 1 | -0.98% | 3/4 | -12.81 | +5.07% |
| net10 | Updates | 32 / 8 | -3.64% | 4/4 | -2718.14 | -3.72% |

The direct deep case is mixed, not evidence that the previous deep-pipeline
regression was resolved. The net10 deep update case is slower in all four pairs
(range -5.34% to -1.34%). Its CPU cost per completed message is higher in three of
four pairs. Lower median p99 does not negate this throughput/CPU concern. These
observations do not establish a causal explanation, a general speedup or a stable
allocation reduction from removing volatile writes.

`timed-details.json` retains every timed case's throughput, CPU microseconds and
bytes per completion, admission/completion p50/p95/p99 upper bounds, drain duration,
forced-GC heap delta and tracking/queue state. `descriptive-details.json` contains
four-repeat medians by variant; these are different statistics from medians of
paired differences in `comparison.json` and must not be interchanged.

## Decision and next action

Keep the simpler bookkeeping because ordinary delivery does not consume that count,
not because this experiment proves a speed improvement. Do not tune more counters
or repeat the same comparison without a new hypothesis grounded in evidence.

The large cost difference between ordinary and revision-enabled deep graphs remains
the more important investigation: in the net10 after runs, median allocation is
about 8.76 KB/completion for direct versus 587.63 KB/completion for updates, with
median CPU about 76.38 versus 3,147.90 microseconds/completion. Those paths have
different policies and are not interchangeable; these figures identify investigation
scope, not permission to remove revision, capture, routing or event behavior.
Profile allocation/diagnostic work across the actual optional-host path before
proposing the next change. Preserve the queue-ownership findings already verified.

Limitations: no affinity/priority controls or significance test; allocation includes
the harness; latency uses logarithmic bucket upper bounds; short warmup does not
prove identical JIT tiering; business drain does not prove complete observation
quiescence or leak freedom. Slow-target, telemetry, cancellation/revision-load and
long-duration retention matrices remain separate gates. No production changes were
made during this measurement turn. Nothing published or sent to external consumers.
