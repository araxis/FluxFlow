# Performance, Concurrency, And Lifetime Baseline

Date: 2026-08-08

## Decision

FluxFlow now has a permanent, non-packable benchmark project and a
deterministic Engine reliability slice. This round preserves every public API,
definition format, package boundary, and delivery guarantee. Production code
was changed only where a before-change measurement exposed repeatable avoidable
allocation.

Benchmark timing is manual evidence, never a CI threshold. Concurrency and
lifetime correctness remain ordinary tests with causal synchronization and
exact assertions.

## Benchmark surface

Project: `benchmarks/FluxFlow.Engine.Benchmarks`

- target: `net10.0`;
- benchmark runner 0.15.8 through central package management;
- entry point: `BenchmarkSwitcher` with caller-supplied filters;
- diagnostics: `MemoryDiagnoser` and rank columns;
- setup: real code-first contracts, definitions, DI provider, Engine startup,
  and typed handles in `GlobalSetup`;
- cleanup: application stop and provider disposal in `GlobalCleanup`;
- measured methods: real `PortRequestResult<T>` or compilation result returns;
- no manual benchmark loops, fake node path, reflection, internal visibility,
  or benchmark-only production hook.

Eight cases cover:

1. addressed unconditional request/reply;
2. typed unconditional request/reply;
3. typed conditional request/reply;
4. a one-hop typed pipeline;
5. an eight-hop typed pipeline;
6. one-link compilation;
7. 32-link compilation;
8. 128-link compilation.

## Hot-path audit

The bounded static scan covered `src/FluxFlow.Engine/Ports` and
`src/FluxFlow.Composition/ApplicationLinks`.

| Recipe | Exact hits | Disposition |
|--------|-----------:|-------------|
| `async void` | 0 | clean |
| `.Result` / parameterless `.Wait()` review | 1 | `Task.Result` is guarded by `IsCompletedSuccessfully`; it does not block |
| `.GetResult()` review | 1 | existing synchronous system-event rejection publication; retained to preserve explicit backpressure semantics without a measured defect |
| `IndexOf` string review | 0 | clean |
| `Substring` | 0 | clean |
| `StartsWith` / `EndsWith` string review | 0 | clean |
| `Contains` string review | 0 | clean |
| parameterless `ToLower` / `ToUpper` | 0 | clean |
| static `Dictionary` | 0 | no frozen-read-table candidate |
| static `FrozenDictionary` inverse | 0 | consistent with no candidate |
| `StringComparer.CurrentCulture` | 0 | clean |
| three-deep `Replace` chain | 0 | clean |
| `params` signatures | 2 | both small diagnostic builders; removed by the measured fix |
| `new List<T>` | 12 | revision/compiler preparation or subscriber snapshots; no unsupported hot-path rewrite |
| `new Dictionary<TKey,TValue>` | 14 | two per-message condition contexts were the measured issue; other hits are construction/revision/compiler state |
| selected LINQ chains | 25 | compiler/revision/lifecycle work plus two small diagnostic builders; the measured builders were removed |
| `ContainsKey` | 4 | compiler graph/port classification, not a same-key hot-path double lookup |

The inverse and cross-file checks confirmed the routing fast path and
diagnostic construction were the only bounded changes justified by the current
measurements. The compiler remains intentionally straightforward and readable;
its work is revision preparation, not per-message execution.

The final inverse scan reports zero `params` signatures, one
`FlowMapContext` construction site, one matching condition dictionary site, 23
selected LINQ chains, and zero frozen dictionaries in the same scope. The one
remaining context/dictionary site is guarded by `IsConditional`; it is required
only when a link actually has a condition. The reduced LINQ count reflects the
two removed diagnostic-builder chains, and the zero frozen-dictionary count is
still consistent with the absence of a static dictionary candidate.

## Before and after

Pre-change ShortRun on the same machine:

- eight-hop typed request: 53.25 KB allocated;
- addressed unconditional request: 13.86 KB allocated;
- typed unconditional request: 14.12 KB allocated.

Final ShortRun:

- eight-hop typed request: 43.29 KB allocated;
- addressed unconditional request: 13.12 KB allocated;
- typed unconditional request: 13.00 KB allocated.

The eight-hop reduction is 9.96 KB, approximately 18.7 percent.

The first internal fix checks `CompiledApplicationLink.IsConditional` before
constructing `FlowMapContext`. Unconditional routes now deliver directly;
conditional routes retain the exact mapping context and failure reporting.

The second replaces two small `params` plus LINQ dictionary builders with
explicit two- and three-field helpers. It keeps diagnostic names, levels,
subjects, attributes, correlation, trace, and causation semantics unchanged.

No pooling, cache, background worker, reflection, global mutable state, new
runtime dependency, or public option was added.

## Final default-job baseline

Environment:

- benchmark runner 0.15.8;
- Windows 11 10.0.26200.8973;
- 12th Gen Intel Core i7-12700H, 20 logical / 14 physical cores;
- .NET SDK 10.0.302;
- .NET runtime 10.0.10, X64 RyuJIT x86-64-v3.

Runtime messaging:

| Case | Mean | Allocated |
|------|-----:|----------:|
| addressed unconditional | 71.62 microseconds | 12.95 KB |
| typed unconditional | 78.45 microseconds | 12.99 KB |
| typed conditional | 75.55 microseconds | 13.49 KB |

Pipeline scale:

| Hops | Mean | Allocated |
|-----:|-----:|----------:|
| 1 | 36.95 microseconds | 7.87 KB |
| 8 | 192.26 microseconds | 43.18 KB |

Link compilation:

| Links | Mean | Allocated |
|------:|-----:|----------:|
| 1 | 6.493 microseconds | 7.69 KB |
| 32 | 64.956 microseconds | 91.04 KB |
| 128 | 274.492 microseconds | 365.33 KB |

Typed and addressed request allocation is equivalent and timing distributions
overlap. The typed authoring/runtime surface therefore shows no meaningful
runtime penalty in this baseline. Conditional routing owns a small explicit
context cost.

## Reliability contract

The focused Engine slice adds deterministic proof for:

- four concurrent batches of sixteen request/reply operations, with every
  value and trace observed exactly once;
- eight compatible revisions with stable port identity, exact routing, and one
  retirement per replaced generation;
- eight rejected revisions with the prior route retained and every candidate
  disposed exactly once; and
- stop while processing is causally held, followed by complete drain of every
  already accepted message and exact one-time disposal.

Existing directly related timing-based tests are converted only when the same
contract can be expressed through causal gates. A bounded `WaitAsync` is a
deadlock guard, not elapsed-time evidence. There are no sleeps, polling loops,
Stopwatch thresholds, throughput assertions, or retry-until-pass behavior.

## Commands and evidence policy

Always run `--job Dry` over `--filter "*"` first. Use ShortRun for iteration and
the default job for a recorded baseline. Redirect verbose output and read the
generated GitHub Markdown reports.

Generated `BenchmarkDotNet.Artifacts` content is ignored. Repository docs store
only the environment and summary necessary to interpret the current baseline.

Correctness tests run in normal CI. Benchmark timing is intentionally manual,
machine-local, and non-blocking for release validation.

## Deferred work

Optional runtime performance telemetry, exported counters, and deeper
contention profiling remain separate future adapter work. The existing
synchronous rejection-to-system-event publication path was manually reviewed
and retained because it carries explicit backpressure semantics; it should not
be changed without focused behavioral and contention evidence.

## Final verification

| Gate | Result |
|------|--------|
| benchmark project build | 5 projects, 0 errors, 0 warnings |
| final dry benchmark validation | 8/8 cases produced reports |
| complete Engine tests | 134 passed, 0 warnings |
| complete Release governance tests | 189 passed, 0 warnings |
| CI-style Release solution build | 137 projects, 0 errors, 0 warnings |
| complete Release solution tests | 2,673 passed across 67 projects, 0 warnings |
| benchmark dependency audit | no known vulnerable direct or transitive package |
| hygiene | whole-worktree diff check and scoped format verification passed |

No package was packed or published, no tag or release was created, and no
external service was contacted by a runtime test.
