# Performance, Concurrency, And Lifetime Baseline

FluxFlow keeps performance work evidence-driven. Runtime behavior is protected
by deterministic correctness tests, while timing and allocation measurements
live in a separate benchmark project. The benchmark results are not CI
pass/fail thresholds: absolute timings vary by machine, runtime, power policy,
and background load.

This round changes no public API, package boundary, workflow definition, or
delivery guarantee. It adds a repeatable measurement surface, strengthens
concurrency and lifecycle proof, and applies only two small allocation fixes
that were visible in the measured message path.

## Permanent benchmark suite

`benchmarks/FluxFlow.Engine.Benchmarks` is a non-packable .NET 10 console
project. It uses the normal code-first component contract, application builder,
Engine lifecycle, typed runtime ports, and real node processing. Setup and host
lifecycle are outside the measured operation.

| Benchmark | Comparison axis | Cases |
|-----------|-----------------|------:|
| `ApplicationMessagingBenchmarks` | addressed versus typed request/reply, plus a typed C# condition | 3 |
| `ApplicationTopologyBenchmarks` | one-hop versus eight-hop typed pipelines | 2 |
| `ApplicationLinkCompilationBenchmarks` | definition/revision preparation at 1, 32, and 128 links | 3 |

The suite has eight cases in total. Message benchmarks return the real
`PortRequestResult<T>` so the work cannot be removed by the JIT. Definitions,
hosts, and nodes are created in `GlobalSetup`; applications are stopped and
providers disposed in `GlobalCleanup`. There are no manual workload loops.

## Running the suite

Build once from the repository root:

```powershell
dotnet build ./benchmarks/FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj -c Release
```

First validate every case with a dry run:

```powershell
dotnet run --project ./benchmarks/FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release --no-build -- `
  --filter "*" --job Dry --noOverwrite
```

Use ShortRun for a quick local comparison:

```powershell
dotnet run --project ./benchmarks/FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release --no-build -- `
  --filter "*" --job Short --noOverwrite
```

Use the default job for a result worth recording. Keep classes separate so a
single command stays bounded:

```powershell
dotnet run --project ./benchmarks/FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release --no-build -- `
  --filter "*ApplicationMessagingBenchmarks*" --noOverwrite

dotnet run --project ./benchmarks/FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release --no-build -- `
  --filter "*ApplicationTopologyBenchmarks*" --noOverwrite

dotnet run --project ./benchmarks/FluxFlow.Engine.Benchmarks/FluxFlow.Engine.Benchmarks.csproj `
  -c Release --no-build -- `
  --filter "*ApplicationLinkCompilationBenchmarks*" --noOverwrite
```

The benchmark runner writes Markdown and CSV reports under
`BenchmarkDotNet.Artifacts`; that generated directory is ignored by Git. Use
`--artifacts <path>` when retaining several named runs for comparison.

## Current local baseline

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

## Measured internal improvements

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

## Deliberate limits

- This is an in-process runtime baseline, not a distributed-system or durable
  store throughput claim.
- Results do not establish a universal service-level objective.
- No automatic regression percentage is enforced in CI.
- Optional runtime telemetry or exported performance counters remain a future,
  separately designed adapter; they are not part of this round.
- The one manually reviewed synchronous system-event publication path keeps its
  existing backpressure semantics. It was not changed without a demonstrated
  defect and should remain part of future contention profiling.

## Verification

The completed round passed:

- all eight benchmark cases in a final dry validation;
- 134/134 complete Engine tests;
- 189/189 complete Release governance tests;
- a warning-free CI-style Release build of 137 projects;
- 2,673/2,673 complete solution tests across 67 projects;
- the benchmark project's direct/transitive vulnerability audit;
- whole-worktree whitespace validation; and
- scoped format verification for every touched C# file.
