# FluxFlow.Fluent

Type-safe, code-first fluent DSL for composing `FluxFlow.Nodes`.

Use this package when you want to wire standalone nodes into a runnable graph in
C# with the compiler checking every connection, instead of canonical string/JSON
application links. The fluent builder produces the same canonical
`ApplicationDefinition` and runs it with an owned `FluxFlowApplication`; it is
a concise facade, not a second workflow runtime.

## Why

`Flow.From(...).Then(...).To(...)` reads as a pipeline, and the generic type
parameter tracks the payload type flowing between nodes: `Then` only accepts a
node whose input matches the current output, so a mis-wired graph is a compile
error, not a runtime diagnostic. The `FlowMessage<T>` envelope stays hidden — you
work in payload types.

## Boundary

`FluxFlow.Fluent` owns:

- the fluent builder (`Flow`, `FlowBuilder<T>`, `FlowTerminal`)
- compile-time-checked linear chains, fan-out (`Tap`), branching (`Branch` from a
  typed output port), and fan-in (share one node instance across branches)
- explicit instance-backed component contracts and canonical typed links
- the built `FlowGraph` (definition, canonical application, start, stop,
  completion, event stream, disposal)

It does not own node implementations, a parallel runtime, JSON/configuration
loading, or persistence. Nodes come from `FluxFlow.Nodes` (and component
packages); lifecycle, stable routing, and staged graph draining come from
`FluxFlow.Engine`.

`ApplicationDefinitionBuilder` remains the general code-first surface for named
resources, workflows, reusable package contracts, typed handles, conditions,
and host-managed revisions. `FluxFlow.Fluent` is the smaller instance-first
surface for `From`/`Then`/`Tap`/`Branch`/`Apply` chains. Both converge on the
same immutable definition and Engine lifecycle. Neither serializes constructed
node instances or C# behavior into JSON.

## Capacity configuration

The fluent graph API links node instances that have already been constructed;
it does not apply a second graph-wide capacity setting. Custom nodes pass their
capacity to the base type explicitly:

```csharp
public sealed class WordSource : FlowSource<string>
{
    public WordSource(IReadOnlyList<string> words)
        : base(new FlowSourceOptions { OutputCapacity = 256 })
    {
        // Store words for RunAsync.
    }
}

public sealed class UppercaseNode : FlowNode<string, string>
{
    public UppercaseNode()
        : base(new FlowNodeOptions
        {
            InputCapacity = 64,
            OutputCapacity = 128
        })
    {
    }

    // ProcessAsync omitted.
}
```

Component-package nodes expose their own immutable options. In the canonical
application DSL, the corresponding component builders use `BoundedCapacity`
or a domain-specific name. Engine `FluxFlowApplicationOptions.OutputCapacity`
is a separate stable-port setting and never overrides these node capacities.

## Linear pipeline

```csharp
await using var flow = Flow
    .From(new WordSource(["alpha", "beta"]))   // FlowSource<string>
    .Then(new UppercaseNode())                 // FlowNode<string, string>
    .To(new CollectSink(collector))            // FlowNode<string, _>
    .Build();

await flow.StartAsync();
await flow.Completion;

var definition = flow.Definition;
var application = flow.Application;
```

## Fan-out, branching, and fan-in

```csharp
var sink = new CollectSink(collector);
var router = new EvenOddRouter();              // FlowNode<int, int> with Even/Odd ports

await using var flow = Flow
    .From(new CountSource(6))
    .Then(router)
    .Tap(new AuditNode())                                          // fan-out, main line unchanged
    .Branch(router.Even, even => even.Then(new LabelNode("even")).To(sink))
    .Branch(router.Odd,  odd  => odd.Then(new LabelNode("odd")).To(sink))  // both fan into one sink
    .Build();

await flow.StartAsync();
await flow.Completion;
```

Branches share the flow's graph; passing the same node instance to `Then`/`To`
in more than one branch fans them into that node. Each node completes once all of
its upstream sources finish, so fan-in drains correctly rather than being
completed early by the first branch.

## Observing events and failures

```csharp
await using var flow = Flow
    .From(new WordSource(["alpha", "beta"]))
    .Then(new RiskyNode())
    .To(new CollectSink(collector))
    .OnEvent(@event => logger.LogInformation("{Name}", @event.Name))
    .Build();
```

`OnEvent` observes the nodes' aggregated event stream. It is also available on
the built `FlowGraph` and returns an `IDisposable` for explicit unsubscribe.
Observation is best-effort, a throwing handler is isolated so it cannot break
the workflow, and subscriptions are torn down with the graph. Expected node
processing failures remain ordinary error-valued `FlowMessage<T>` data;
terminal source/node faults surface through `Completion`.

## Reusable named sub-flows

```csharp
var normalize = FlowSegment.Define<string, string>("normalize",
    b => b.Then(new TrimNode()).Then(new UppercaseNode()));

await using var flow = Flow
    .From(new WordSource(["  alpha ", "beta"]))
    .Apply(normalize)          // splice the segment in
    .To(new CollectSink(collector))
    .Build();
```

A `FlowSegment<TIn, TOut>` is a named, typed fragment you define once and splice
into any flow with `Apply`. It holds the build delegate, not node instances, so
each application constructs fresh nodes — the same segment is safe to reuse
across graphs and to apply more than once.

## Sample

```sh
dotnet run --project samples/FluxFlow.FluentSample/FluxFlow.FluentSample.csproj
```
