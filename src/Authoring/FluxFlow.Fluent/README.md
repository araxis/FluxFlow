# FluxFlow.Fluent

Readable pipelines built from direct component instances. The built graph owns
one Core `FlowApplication`; it does not create a private service provider,
executable descriptors, or another application host.

All component ports use canonical `FlowMessage` values. Each component
materializes its own input locally. The builder does not maintain a parallel
typed transport or promise compile-time payload-shape checking.

## Direct pipelines

```csharp
await using var flow = Flow
    .From(new WordSource(["alpha", "beta"]), "words")
    .Then(new UppercaseNode(), "uppercase")
    .To(new CollectSink(collector), "collector")
    .Build();

await flow.StartAsync();
await flow.Completion;
```

Names are optional; omitted names become `node0001`, `node0002`, and so on in
the `main` workflow. `flow.Application.GetInput("main.collector.Input")`
resolves the real Core input address. The graph's completion is the Core
completion task, available after startup. Stop and disposal also delegate to
Core, including disposal before a source starts.

Component capacities are chosen in their constructors/options. The fluent layer
adds no processing queues. Standard components use their existing Core output
dispatcher for graph routing. A custom native source can require Core's bounded
source adapter; this is not an additional stage for every ordinary component.

## Branches, side outputs and shared sinks

```csharp
var sink = new CollectSink(collector);
var router = new EvenOddRouter();

await using var flow = Flow
    .From(new CountSource(6))
    .Then(router)
    .Branch(router.Even, branch => branch.Then(new LabelNode("even")).To(sink))
    .Branch(router.Odd, branch => branch.Then(new LabelNode("odd")).To(sink))
    .Build();
```

A branch output must be declared in the attached component's `FlowPorts`.
There is no artificial branch node or signal dependency. The same sink instance
is registered once; Core waits for all its producers before completing it,
including empty branches. `Tap(node)` feeds a side component while leaving the
main continuation unchanged. Fanout retains the canonical message instance.

Unmatched business output faults the graph with its port address, just as in
direct/document routing. `Build()` does not silently discard unlinked outputs.
`To(node)` and `Tap(node)` explicitly designate that component as terminal: its
declared data outputs discard unmatched messages, but any explicit links still
receive matching messages. Input-only sinks work without an output port.

Use `DiscardUnmatched()` to express discard intent for the current continuation
or one selected branch, for example:

```csharp
Flow.From(new CountSource(6))
    .Then(router)
    .Branch(router.Even, branch => branch.Then(new LabelNode("even")).To(sink))
    .Branch(router.Odd, branch => branch.DiscardUnmatched())
    .Build();
```

This policy applies only when no route matches; it does not disconnect links or
affect another component's outputs. For migration, replace an intentionally
terminal `.Then(node).Build()` with `.To(node).Build()`, or explicitly call
`.DiscardUnmatched()` on the intended output. An unused output that never emits
does not fault the graph.
A builder can be built once and cannot be changed afterward.

## Reusable segments

```csharp
var normalize = FlowSegment.Define("normalize",
    builder => builder.Then(new TrimNode()).Then(new UppercaseNode()));

await using var flow = Flow.From(new WordSource([" alpha "]))
    .Apply(normalize)
    .To(new CollectSink(collector))
    .Build();
```

Construct fresh instances inside a segment. A segment must return a continuation
on the same graph; it cannot replace that graph with an unrelated builder.

## Events and hosting

`OnEvent(handler)` is an optional convenience over native event links.
Observation is bounded and best-effort; slow observers may miss events and
throwing handlers do not fault workflow processing. `flow.Events.LinkTo(...)`
remains available for direct native observation. Errors remain normal canonical
message data; terminal runtime failures surface through `Completion`.

Install the optional hosting package to use `AddFlowGraph`. The host may resolve
dependencies before constructing components; the fluent graph itself does not
create a dependency container.

## Portable documents

Reference Composition and import `FluxFlow.Composition.Authoring` to use
`ApplicationDefinitionBuilder.BuildApplicationAsync(bindings)` for the portable
construction path. That extension belongs to optional code authoring over JSON, not
this direct pipeline package. Fluent itself references only Core among the
framework projects. Arbitrary runtime instances and delegates are not silently serialized:
the direct pipeline no longer exposes an executable `Definition`.
The portable model/parser are owned by Json, independently of code authoring.
See the execution ledger for remaining capability and registration/editor audits.

Version 7.0.0-alpha.1 is an unpublished development preview, not a release candidate.
The executable example is in `samples/FluxFlow.FluentSample`.
