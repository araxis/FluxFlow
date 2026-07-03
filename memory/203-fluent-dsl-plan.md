# Fluent DSL Plan

Date: 2026-07-03

## Goal

Add `FluxFlow.Fluent`: a powerful, type-safe, code-first DSL for composing
standalone nodes, complementing the existing string/JSON `CompositionDefinitionBuilder`.
The compiler guarantees output→input type compatibility, options are typed
constructor args (no `JsonElement`), and lifecycle/errors/disposal are handled.
Decided scope (2026-07-03, user): build the whole design in one pass, and reuse
`FluxFlow.Composition`'s `CompositionRuntime` for the runtime.

## Motivation

Today there are two authoring paths: direct code (`new Node()` + `LinkTo`, fully
typed but manual lifecycle) and the definition+registry path (persistable but
stringly-typed: string node types, `"a.Output"` links, `JsonElement` options,
type mismatches caught only at runtime-build). Neither is a low-ceremony
compile-checked code-first experience. The fluent DSL fills that gap and is a
library-level win (not UI — see `[[designer-ui-low-priority]]`).

## Runtime reuse seam (in FluxFlow.Composition)

`CompositionRuntime`'s lifecycle (start `IFlowSource`s, `Task.WhenAll`
completions, aggregate `Errors`/`Events` broadcasts, ordered dispose, stop by
completing entry nodes) depends only on each node's `ComposedNode` descriptor +
links + which nodes are entry (no-incoming) nodes; `RuntimeNodeKey`/
`NodeDefinition` are unused metadata. Add one additive public factory:

    public static CompositionRuntime Create(
        IReadOnlyList<ComposedNode> nodes,
        IReadOnlyList<IDisposable> links,
        IReadOnlyList<ComposedNode> entryNodes)

It synthesizes internal keys/definitions and computes `nodesWithIncomingLinks`
from the entry set. Minor version bump for `FluxFlow.Composition` + public-API
baseline re-accept.

## The API

Generic `FlowBuilder<T>` carries the current output payload type plus a shared
graph accumulator (nodes, links, entry set). The `FlowMessage<T>` envelope stays
hidden.

    await using var run = Flow
        .From(new IntervalTimer(every: TimeSpan.FromSeconds(5)))   // FlowSource<DateTimeOffset>
        .Then(new Mapper<DateTimeOffset,string>(t => t.ToString()))// TIn must equal prior TOut
        .Then(new UppercaseNode())                                 // FlowNode<string,string>
        .To(new CollectSink<string>())                             // terminal
        .Build();
    await run.StartAsync();
    await run.Completion;

- `Flow.From(FlowSource<T>)` starts a pipeline; `.Then(FlowNode<T,TNext>)` links
  `currentOutput → node.Input` (PropagateCompletion) and returns
  `FlowBuilder<TNext>`; `.To(FlowNode<T,_>)` links and terminates.
- The built `FlowGraph` wraps a `CompositionRuntime` (StartAsync/StopAsync/
  Completion/Errors/Events/DisposeAsync).
- No reflection/registry: pass constructed nodes; `Errors`/`Events` are read from
  the concrete `FlowNode<,>`/`FlowSource<>` the typed methods accept.

## Powerful features (full one-pass design)

- **Fan-out / tap** — `Output` is a `BroadcastBlock`, so
  `.Broadcast(b => …, b => …)` / `.Tap(node)` link one output to many branches.
- **Multi-output branching** (Switch/Fork/Filter pass-fail/EvenOdd) — branch by
  starting a sub-builder from a specific typed port:
  `Flow.From(node.Odd)…` sharing one accumulator. Each port is typed, so
  branches stay compile-checked.
- **Fan-in** — `.Merge(builderA, builderB)` / a join builder link multiple
  upstreams into one node.
- **Errors/events** — the graph surfaces aggregated `Errors`/`Events`, with an
  optional `.OnError(handler)`.
- **DI/hosting** — `Flow.From(sp => new Node(sp.GetRequiredService<…>()))`
  overloads; an optional hosted runner (mirrors `Composition.Hosting`).
- **Reusable sub-flows** — named sub-pipelines composed into larger graphs.

## Package and release wiring

New `src/FluxFlow.Fluent` (`net8.0;net10.0`, `PackageId`, references
`FluxFlow.Nodes` + `FluxFlow.Composition`; shared icon via
`Directory.Build.targets`). Add to `FluxFlow.sln`, `eng/packages.json`,
`CHANGELOG.md`, `docs/14-public-api-overview.md`, and the public-API baseline;
add a `samples/FluxFlow.FluentSample` and list it in `docs/README.md`.

## Testing

- Deterministic pipeline tests (drive sources with `FakeTimeProvider`, assert
  collected outputs).
- A compile-time negative test project proving a mismatched `.Then` does not
  compile.
- Lifecycle/cancellation/fault/broadcast/branch/merge coverage, matching the
  `FluxFlow.Nodes.Tests` / `FluxFlow.Composition.Tests` style.

## Build order

1. `CompositionRuntime.Create` seam (+ Composition minor bump, baseline accept).
2. `FluxFlow.Fluent` core: `Flow`, `FlowBuilder<T>`, graph accumulator,
   `FlowGraph`; linear `From/Then/To`; first passing test.
3. Broadcast/tap.
4. Multi-output branching + fan-in (merge/join).
5. Errors/events surface + `.OnError`.
6. DI overloads + optional hosted runner.
7. Reusable sub-flows.
8. Sample + docs + full release wiring.

## Risks / open

- Branching type-safety ergonomics (keep it fluent AND typed).
- The reuse seam is additive but changes `FluxFlow.Composition`'s public API.
- Naming: `Flow` / `FlowBuilder` / `FlowGraph`.

## Status (2026-07-03, branch `work/fluent-dsl`)

Built and shipped as `FluxFlow.Fluent 1.0.0` across three commits: (1) the seam +
linear `From/Then/To/Build` + `Tap`; (2) `Branch` from typed ports + fan-in via
shared node instances, with the explicit "complete a node once all upstreams
finish" model replacing per-link `PropagateCompletion` (the correct choice for
fan-in); (3) packaging (manifest, changelog, docs/14, baseline `55|21`, README,
icon) + `samples/FluxFlow.FluentSample`. Public surface: `Flow`, `FlowBuilder<T>`,
`FlowTerminal`, `FlowGraph`. 9 tests, branch/fan-in flake-checked 30x, full
release suite green (92). Naming resolved to `Flow`/`FlowBuilder`/`FlowTerminal`/
`FlowGraph`. Merged to `main` (PR #57) and released to nuget.org as
`composition-v1.2.0` + `fluent-v1.0.0` (published, indexed, GitHub releases
created).

Follow-on shipped: `OnError`/`OnEvent` observation on
`FlowBuilder`/`FlowTerminal`/`FlowGraph` (handler-isolated, torn down with the
graph) — released to nuget.org as `FluxFlow.Fluent 1.1.0` (`fluent-v1.1.0`,
PR #59; baseline `55|29`).

Deliberately NOT built (KISS / library-focus, see [[designer-ui-low-priority]]):
`Flow.From(sp => ...)` DI factory overloads (users already pass constructed
nodes), a hosted-runner
(belongs in a separate `FluxFlow.Fluent.Hosting` package like
`Composition.Hosting`), and reusable named sub-flows. All are additive follow-ons
if a real need appears.
