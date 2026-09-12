# Current candidate condition costs

Candidate: `0.0.0-local.20260911.startup.1`. This qualification measures the
existing condition benchmark against the exact local package assemblies. It
closes an outdated-source gap; it is not an original-to-final speedup claim.

## Workload and verification

The [existing benchmark](../../benchmarks/FluxFlow.Engine.Benchmarks/GraphConditionBenchmarks.cs)
is linked unchanged into a task-local package-only runner. It sends one reused
structured scalar through 1, 8 or 32 pass-through nodes to two bounded targets.
Every conditional route evaluates the selected condition; a graph with N nodes
therefore evaluates N+1 predicates for each successful message. Setup checks
reference-identical delivery, false-condition fallback and successful delivery
after rejection. Expression parsing/preparation occurs during graph setup.

The modes are unconditional, native C# predicate, JSONata document expression
(`input > 0`) and C# expression with a native-configured `positive` function.
These are specific expressions, not a ranking of the engines for every workload.
No listener or SDK exporter is installed by this fixture.

Release build, default pilot/warmup/iteration policy, two independent process
launches per measured case, no power-plan changes. Other builds/load tests were
not launched concurrently. Ordinary background machine activity is uncontrolled.
Dry runs are correctness checks, not timing evidence. A default eight-hop
unconditional measurement precedes each full repeated batch.

Machine: Core i7-12700H, Windows 11, SDK 10.0.400. Frameworks use their installed
runtime: .NET 10.0.11 and .NET 8.0.30. Compare modes within each framework; this
experiment does not isolate the reasons for cross-runtime differences.

All 1,488 qualified source inputs are checked before and after each run. All five
FluxFlow dependencies match the candidate hashes in both the runner and the
generated process outputs. Package references avoid silently treating rebuilt
project dependencies as exact package binaries. Failed setup/qualification
attempts remain recorded and are excluded from successful counts.

## .NET 10 results

Twelve dry cases, one representative default case, and twelve repeated measured
cases passed. The measured cases executed 24 separate processes. Means below
are microseconds per complete graph operation; allocations are bytes per operation.

| Hops | Mode | Mean µs | 99.9% confidence margin µs | Bytes/op |
| ---: | --- | ---: | ---: | ---: |
| 1 | Unconditional | 3.97 | 0.04 | 1,272 |
| 1 | Native predicate | 4.20 | 0.06 | 1,272 |
| 1 | Document expression | 21.25 | 0.29 | 20,215 |
| 1 | Configured expression | 5.58 | 0.08 | 3,896 |
| 8 | Unconditional | 23.22 | 0.34 | 3,833 |
| 8 | Native predicate | 23.85 | 0.31 | 3,848 |
| 8 | Document expression | 71.80 | 1.06 | 88,886 |
| 8 | Configured expression | 29.82 | 0.50 | 15,661 |
| 32 | Unconditional | 66.58 | 0.87 | 12,495 |
| 32 | Native predicate | 68.37 | 0.52 | 12,495 |
| 32 | Document expression | 236.85 | 3.82 | 324,280 |
| 32 | Configured expression | 86.74 | 1.10 | 55,793 |

Exact measurements, per-launch means and allocations:
[summary](../../artifacts/core-refactoring/current-conditions-20260911/net10.0/summary.json).

## .NET 8 results

The same twelve dry cases, one representative case and twelve repeated cases
passed, with 24 measured processes. Across both frameworks, the complete batch
therefore has 24 qualified cases / 48 measured process launches, plus 24 dry
processes and two representative processes. These are benchmark executions, not
additional unit-test counts.

| Hops | Mode | Mean µs | 99.9% confidence margin µs | Bytes/op |
| ---: | --- | ---: | ---: | ---: |
| 1 | Unconditional | 4.33 | 0.06 | 1,280 |
| 1 | Native predicate | 4.43 | 0.05 | 1,280 |
| 1 | Document expression | 27.12 | 0.74 | 20,394 |
| 1 | Configured expression | 6.50 | 0.09 | 4,016 |
| 8 | Unconditional | 26.40 | 0.36 | 3,910 |
| 8 | Native predicate | 26.98 | 0.41 | 3,913 |
| 8 | Document expression | 82.16 | 1.14 | 89,671 |
| 8 | Configured expression | 33.15 | 0.44 | 16,227 |
| 32 | Unconditional | 71.54 | 0.99 | 12,752 |
| 32 | Native predicate | 72.00 | 1.40 | 12,751 |
| 32 | Document expression | 266.85 | 3.01 | 327,176 |
| 32 | Configured expression | 93.96 | 1.07 | 57,896 |

Exact measurements and per-launch results:
[summary](../../artifacts/core-refactoring/current-conditions-20260911/net8.0/summary.json).

## Interpretation and limits

Native predicates add little allocation in the measured .NET 10 workload. Adapter
execution is materially more expensive: approximately 1.31 KB additional allocation
per configured predicate and 9.45 KB per document predicate relative to the
unconditional graph. The corresponding .NET 8 figures are approximately 1.37 KB
and 9.53 KB. These are whole-graph allocation differences divided by the
number of predicate calls, not allocation-stack attribution or exact per-call laws.

The document adapter retains compiled queries but constructs a fresh environment
and converts/binds input for each evaluation. Its native functions can mutate
tokens, so root/binding isolation is intentional. Those source-backed operations
are plausible contributors, not a profile proving how the total is distributed.
Do not remove isolation, introduce shared mutable state, or add Core machinery
solely to lower these numbers. Native predicates remain the simplest code-only
choice; configurable document expressions remain available as an optional capability.

No original/final regression conclusion follows from this within-candidate table.
It does not measure individual-message p50/p95/p99, maximum sustainable throughput,
large/binary payloads, saturated queues, retained heap, expiry/fault recovery,
revision swaps or telemetry exporters. Those separate existing gates retain their
own evidence and remaining work; a full Cartesian product is not newly required.

Reproduction and preserved unsuccessful attempts are described in the
[workload plan](../../artifacts/core-refactoring/current-conditions-20260911/PLAN.md).
The runner and summarizer retain exact commands, source identities, dependency
hashes, process counts and full benchmark reports. No production code, existing
benchmark body, test, or package changed. No package was published or repacked.
