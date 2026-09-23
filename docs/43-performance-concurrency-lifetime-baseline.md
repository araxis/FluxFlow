# Performance, Concurrency, And Lifetime Baseline

FluxFlow keeps performance work evidence-driven. Runtime behavior is protected
by deterministic correctness tests, while timing and allocation measurements
live in a separate benchmark project. The benchmark results are not CI
pass/fail thresholds: absolute timings vary by machine, runtime, power policy,
and background load.

The historical August round changed no public API, package boundary, workflow definition, or
delivery guarantee. It added a repeatable measurement surface, strengthened
concurrency and lifecycle proof, and applied only two small allocation fixes
that were visible in the measured message path. The separate September core refactor
permits breaking changes; its fresh baseline and progress are recorded in the
[execution ledger](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/docs/plans/2026-09-08-core-refactoring-progress.md).

## Permanent benchmark suite

`benchmarks/FluxFlow.Engine.Benchmarks` is a non-packable .NET 8/.NET 10 console
project. The message-delivery and pipeline fixtures exercise the optional update
host and typed caller conveniences. The canonical delivery fixtures exercise
native links and ordinary instance-first Core applications with real nodes.
Do not treat these different contracts as interchangeable before/after results.
Setup and host lifecycle are outside the measured operation.

| Benchmark | Comparison axis | Cases |
|-----------|-----------------|------:|
| `ApplicationMessagingBenchmarks` | addressed versus typed send/receive, plus a typed C# condition | 3 |
| `ApplicationTopologyBenchmarks` | typed pipelines at 1, 8, and 32 hops | 3 |
| `ApplicationLinkCompilationBenchmarks` | portable link compilation at 1, 32, and 128 links | 3 |
| `FlowValueDeliveryBenchmarks` | native versus public application delivery at 1/8/32 hops and 1/2/8 targets | 18 |
| `ExpressionBindingBenchmarks` | one already-compiled condition over 8/256 structured items | 2 |
| `PayloadSizeDeliveryBenchmarks` | ordinary application delivery; 1/8/32 hops, 1/2/8 targets, structured/binary payloads of 1 KiB/1 MiB | 36 |
| `GraphConditionBenchmarks` | canonical graph with no condition, native predicate, and both configured expression adapters; 1/8/32 hops and two targets | 12 |

The suite has 77 configured cases for one framework/job; this count is not a claim that
every case was measured in the latest checkpoint. Application message benchmarks return the real
`PortReceiveResult<T>` after separate send/receive operations; these measurements
do not include correlated request/reply. Canonical delivery returns the actual received envelope.
Definitions,
hosts, and nodes are created in `GlobalSetup`; applications are stopped and
providers disposed in `GlobalCleanup`. There are no artificial invocation loops;
each canonical delivery invocation receives one message from each actual fanout target.

The payload-size fixture checks shared message/value identity and complete content
at every target before and after measurement. Structured size is the ASCII body
length plus fixed fields; binary size is the original content length before its
canonical encoding. Safe ingress copying and final materialization are outside
timing. Allocated bytes per round trip are not retained heap, saturated throughput,
or per-message p95/p99 latency. No listeners are installed by these delivery fixtures.

The graph-condition fixture includes actual portable document context binding in
the measured path, with parsing and compilation confined to setup. All measured
variants forward the same positive scalar payload to two targets. Setup separately
checks negative routing, fallback exclusivity, original-envelope identity, and
delivery after rejection. Comparing this fixture with an older topology that lacks
fallback routes is not a controlled before/after performance comparison.

## Running the suite

Build once from the repository root:

```powershell
dotnet build ./benchmarks/FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj -c Release
```

Run from the benchmark directory so the scoped solution confines project discovery
and excludes any frozen release checkout. First validate every case with a dry run:

```powershell
Set-Location ./benchmarks
dotnet run --project ./FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release -f net10.0 --no-build -- `
  --filter "*" --job Dry --noOverwrite
```

Use a Short job for a quick local comparison:

```powershell
dotnet run --project ./FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release -f net10.0 --no-build -- `
  --filter "*" --job Short --noOverwrite
```

Use the default job for a result worth recording. Keep classes separate so a
single command stays bounded:

```powershell
dotnet run --project ./FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release -f net10.0 --no-build -- `
  --filter "*ApplicationMessagingBenchmarks*" --noOverwrite

dotnet run --project ./FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release -f net10.0 --no-build -- `
  --filter "*ApplicationTopologyBenchmarks*" --noOverwrite

dotnet run --project ./FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release -f net10.0 --no-build -- `
  --filter "*ApplicationLinkCompilationBenchmarks*" --noOverwrite

dotnet run --project ./FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release -f net10.0 --no-build -- `
  --filter "*FlowValueDeliveryBenchmarks*" --noOverwrite

dotnet run --project ./FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release -f net10.0 --no-build -- `
  --filter "*ExpressionBindingBenchmarks*" --noOverwrite --exporters json

dotnet run --project ./FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release -f net10.0 --no-build -- `
  --filter "*PayloadSizeDeliveryBenchmarks*" --noOverwrite --exporters json

dotnet run --project ./FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release -f net10.0 --no-build -- `
  --filter "*GraphConditionBenchmarks*" --noOverwrite --exporters fulljson
```

Choose `-f net8.0` to measure the other supported framework. A single-framework
run keeps condition ratios relative to that framework's unconditional baseline.
For cross-runtime dry validation, use `--runtimes net8.0 net10.0 --job Dry`; runtime
jobs multiply the configured case count. Redirect verbose output to a unique log.

The benchmark runner writes Markdown and CSV reports under
`BenchmarkDotNet.Artifacts`; that generated directory is ignored by Git. Use
`--artifacts <path>` when retaining several named runs for comparison.
The runner leaves the existing power plan unchanged and preserves CLI job selection.
Record the active plan and environment, avoid concurrent build/test activity, and
validate a representative Default case before a full measured run. Inspect reports
for actual case counts, nonempty statistics and process/cleanup failures: a zero
launcher exit code alone does not prove that any benchmarks executed.

The current refactoring evidence and outstanding performance gates are indexed by
the execution ledger linked above. August results below remain historical.

## Historical local baseline: August

Recorded on 2026-08-08 with benchmark runner 0.15.8, Windows 11, a 12th Gen Intel
Core i7-12700H, .NET SDK 10.0.302, and .NET runtime 10.0.10. These numbers
describe this machine only.

### Runtime messaging

| Case | Mean | Allocated |
|------|-----:|----------:|
| addressed unconditional request | 71.62 microseconds | 12.95 KB |
| typed unconditional request | 78.45 microseconds | 12.99 KB |
| typed conditional request | 75.55 microseconds | 13.49 KB |

The timing distributions overlap and the typed/addressed allocation is
equivalent. Typed handles therefore do not show a meaningful runtime tax in
this baseline. A real C# condition adds about 0.5 KB for its condition context.

### Pipeline scale

| Hops | Mean | Allocated |
|-----:|-----:|----------:|
| 1 | 36.95 microseconds | 7.87 KB |
| 8 | 192.26 microseconds | 43.18 KB |

### Link compilation scale

| Links | Mean | Allocated |
|------:|-----:|----------:|
| 1 | 6.493 microseconds | 7.69 KB |
| 32 | 64.956 microseconds | 91.04 KB |
| 128 | 274.492 microseconds | 365.33 KB |

Compilation is a definition/revision preparation cost, not a per-message cost.
The suite intentionally reports it separately from runtime throughput.

## Historical internal improvements: August

The pre-change ShortRun allocated 53.25 KB for the eight-hop request. The final
ShortRun allocates 43.29 KB, a reduction of approximately 18.7 percent.

Two internal changes account for that result:

1. Unconditional links no longer allocate a `FlowMapContext` and three-entry
   dictionary. Conditional links still build the exact context required by
   expression and typed-predicate semantics.
2. Runtime activity/request diagnostics no longer allocate `params` arrays and
   LINQ iterators merely to construct their small two- or three-field attribute
   dictionaries.

Both changes preserve existing routing, condition failure isolation,
diagnostic content, and lifecycle behavior. No new cache, pool, reflection,
background worker, global state, or dependency was introduced.

## Correctness under pressure

Timing is never used as correctness evidence. Engine tests use causal gates and
exact counts to verify concurrent request batches, bounded backpressure and
cancellation, repeated compatible and rejected revisions, retained active
routes, shutdown with accepted work in flight, and exact disposal/retirement.
Bounded `WaitAsync` calls may guard against a deadlock, but elapsed time is not
asserted and there are no sleeps, polling loops, throughput thresholds, or
retry-until-green behavior.

Normal CI continues to run the deterministic tests. Benchmark jobs remain
manual evidence and should be compared only on the same controlled machine.

## Deliberate limits of the August round

- This is an in-process runtime baseline, not a distributed-system or durable
  store throughput claim.
- Results do not establish a universal service-level objective.
- No automatic regression percentage is enforced in CI.
- Optional runtime telemetry or exported performance counters remain a future,
  separately designed adapter; they are not part of this round.
- The one manually reviewed synchronous system-event publication path keeps its
  existing backpressure semantics. It was not changed without a demonstrated
  defect and should remain part of future contention profiling.

## Historical verification: August

The completed round passed:

- all eight benchmark cases in a final dry validation;
- 134/134 complete Engine tests;
- 189/189 complete Release governance tests;
- a warning-free CI-style Release build of 137 projects;
- 2,673/2,673 complete solution tests across 67 projects;
- the benchmark project's direct/transitive vulnerability audit;
- whole-worktree whitespace validation; and
- scoped format verification for every touched C# file.
