# Repeated payload and fanout load

The unchanged current load harness passed **12 smoke and 24 measured processes**
against startup.1-qualified binaries on both supported frameworks. Measured totals
were **67,640,294 completed messages and 282,755,940 target deliveries**. Every
target received the original envelope reference, with exact per-target counts,
no duplicate deliveries or rejected reliable admissions, and empty tracking/input
queues/active handlers after drain.

This completes the outstanding representative direct payload/topology repetition
work. It does not replace the separately documented original-to-current native-link
regression, conditions, telemetry, optional-host or external integration evidence.

## Workload and results

Each process uses four producers, capacity 16, 64 serial warmup messages, and ten
seconds of load followed by drain. The graph uses named direct application routes;
forwarding and sink handlers perform no business processing or external I/O. Tracing
is off, with no installed listener or input tracing context. Payload construction
and fingerprinting occur before timing. Structured sizes describe ASCII data
characters plus a small JSON wrapper; binary sizes describe actual content bytes.

Six scenarios deliberately cover 1/8/32 hops and 1/2/8 targets. The four deep cases
compare both payload sizes and shapes at equal topology/capacity/producer settings.
The second repetition reverses scenario and framework order. Processes run serially;
no competing task-started build or load ran during measurement. The existing High
performance power plan remained unchanged. This is representative coverage, not
an exhaustive Cartesian matrix.

Each table cell shows **first / second** process results. Throughput counts input
messages completed at every target, not individual target deliveries.

| Framework | Scenario: hops/targets, shape, size | Completed/s | All-target p99 upper ms | Whole-workload bytes/completed message |
| --- | --- | ---: | ---: | ---: |
| 8 | 1/1, structured, 256 | 615,811 / 557,138 | 0.078 / 0.096 | 1,027 / 1,039 |
| 8 | 8/2, structured, 65,536 | 409,372 / 356,262 | 1.544 / 2.161 | 2,597 / 2,682 |
| 8 | 32/8, structured, 256 | 176,912 / 159,257 | 12.770 / 14.861 | 8,435 / 8,481 |
| 8 | 32/8, structured, 65,536 | 177,803 / 157,887 | 14.386 / 15.859 | 8,547 / 8,493 |
| 8 | 32/8, binary, 256 | 174,135 / 162,189 | 17.673 / 18.060 | 8,625 / 8,551 |
| 8 | 32/8, binary, 65,536 | 178,425 / 158,465 | 12.770 / 16.924 | 8,663 / 8,656 |
| 10 | 1/1, structured, 256 | 636,049 / 580,008 | 0.075 / 0.065 | 1,027 / 1,027 |
| 10 | 8/2, structured, 65,536 | 378,787 / 359,301 | 1.857 / 2.003 | 2,638 / 2,609 |
| 10 | 32/8, structured, 256 | 186,853 / 193,774 | 16.207 / 15.187 | 8,820 / 8,784 |
| 10 | 32/8, structured, 65,536 | 183,590 / 194,233 | 14.861 / 12.909 | 8,650 / 8,660 |
| 10 | 32/8, binary, 256 | 180,263 / 192,975 | 15.688 / 14.078 | 8,673 / 8,730 |
| 10 | 32/8, binary, 65,536 | 181,964 / 184,215 | 15.187 / 16.383 | 8,850 / 8,745 |

Runtime versions are .NET 8.0.30 / 10.0.11, Release x64, workstation GC.
Latencies are logarithmic histogram upper bounds for individual messages from
registration through all-target completion, including admission and queue waiting.
Allocation includes envelope creation, delivery tracking, histograms and sampling.
Raw reports also retain p50/p95, admission latency, CPU, drain duration and queue/
heap samples. These are not isolated library costs or application throughput/p99
guarantees. Variation between two process repetitions is visible; no confidence
interval, runtime ranking or before/after speedup is inferred from this table.

## Payload-size allocation and reference sharing

At the same 32-hop/eight-target topology, increasing data size from 256 to 65,536
changed mean allocation across the two repetitions as follows:

| Framework / shape | Small mean bytes/message | Large mean bytes/message | Difference | Change |
| --- | ---: | ---: | ---: | ---: |
| 8 / structured | 8,458.00 | 8,519.68 | +61.68 | +0.73% |
| 8 / binary | 8,587.94 | 8,659.57 | +71.63 | +0.83% |
| 10 / structured | 8,801.93 | 8,655.45 | −146.47 | −1.66% |
| 10 / binary | 8,701.70 | 8,797.68 | +95.98 | +1.10% |

No payload-sized per-branch allocation growth was observed. More directly, the
existing harness checks `ReferenceEquals(originalMessage, deliveredMessage)` at
every target and rejects duplicates, unknown messages and incomplete accounting.
Matching payload fingerprints across corresponding runs/frameworks additionally
check that the prepared workload is consistent. The load fixture does not decode
binary content again at each target; existing immutable ownership/binary tests
establish that contract. This does not make ingress ownership copies or processing
that creates derived results allocation-free.

Observed maximum tracked messages / conservative bound were **53/53** for 1/1,
**284/300** for 8/2, and **1,076/1,188** for 32/8. Tracking covers in-flight producer
and graph messages; it is not the request/reply registry. Sampled queue counters
are not transactional. Graph-alive heap after drain includes retained graph and
harness state; it is not a leak or collectibility assertion. See the separate
[retired-state qualification](2026-09-11-request-expiry-fault-retention.md).

## Evidence and disposition

- [Workload plan](../../artifacts/core-refactoring/payload-repetitions-20260911/PLAN.md)
  and [exact command runner](../../artifacts/core-refactoring/payload-repetitions-20260911/run.ps1).
- [Qualified batch](../../artifacts/core-refactoring/payload-repetitions-20260911/verification.json):
  1,488 unchanged source inputs, 102 unchanged runtime files, 14 production DLL
  copies matching startup.1 package inventory, unchanged runner and power plan.
- [Summary](../../artifacts/core-refactoring/payload-repetitions-20260911/summary.json)
  and [ordered raw report references/hashes](../../artifacts/core-refactoring/payload-repetitions-20260911/runs.json).
  Independent summary checks verified all 36 report hashes, accounting, paired
  scenarios, binary/source stability and derived values.
- Existing [load runner](../../benchmarks/FluxFlow.Engine.Benchmarks/LoadQualification/LoadRunner.cs)
  and [identity/accounting assertions](../../benchmarks/FluxFlow.Engine.Benchmarks/LoadQualification/LoadMeasurements.cs)
  were inspected and executed unchanged. The --load entry point does not run the
  separate microbenchmark engine.

No new C# benchmark, permanent test, production change, build, restore or package
mutation was needed. This batch reuses the already-qualified current build and
verifies its identities. Smokes add 1,536 count-limited messages, excluded from
the measured totals. No package publication or external consumer coordination.

The [performance coverage ledger](2026-09-11-performance-evidence-coverage.md)
credits this completed representative scope. Optional-host request/update load,
remaining observer/exporter boundaries and their retention remain distinct; do
not rerun this matrix merely to postpone those requirements. Overall goal active.
