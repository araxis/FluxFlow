# Explicit component composition

## Result

Components now construct their native Dataflow blocks and connections in their own
constructors. FlowNode only contains emission helpers and lifecycle forwarding. It
does not construct inputs, outputs or a coordinator, and has no processing or source
factory methods. Components can still implement IFlowNode directly.

One FlowLifetime replaces FlowProcessingLifetime and FlowSourceLifetime. It accepts
existing blocks through HasInput, HasOutput, HasEvents and HasTerminalInput. These
methods declare lifecycle participation; they do not construct, name or link ports.
Processing and producer callbacks remain ordinary functions. No additional base,
registration model or block factory abstraction was introduced.

Batch now explicitly constructs its TransformManyBlock, broadcast Output and Events,
links input results to Output, and supplies its final-flush callback:

```csharp
Lifetime = new FlowLifetime(this, flush: OnInputCompletedAsync,
    cancellationToken: execution.CancellationToken);
// Construct input and connect its results to Output.
Lifetime.HasInput(input);
Lifetime.HasOutput(Output);
Lifetime.HasEvents(Events);
Lifetime.Observe();
```

Observe seals declarations at the end of construction. It prevents an already
canceled or faulted input from completing the coordinator before the other blocks
have been declared. It does not start source production; IFlowSource.StartAsync
still controls that phase. Declarations cannot change after observation begins.

## Preserved behavior

- Native TransformManyBlock controls execution options and returned-result ordering.
- FlowBlockExtensions.ProcessMessageAsync supplies tracing and processing-error
  conversion without constructing a block. Returned results are materialized before
  publication; explicit-emission callbacks forward errors through the same immediate
  output path.
- Completion drains every ordinary input before flushing and finishing data outputs.
  The first declared ordinary input is the main input whose natural completion starts
  this drain. Faults also join active inputs before component resources are released.
- Reply inputs stay available while accepted processing drains. Source production
  completion remains distinct from full completion so late replies can finish.
- Faults preserve exception identity and cancel processing. Event outputs drain;
  source cancellation callbacks are joined. Disposal releases owned resources once.
- Broadcast publication retains its existing semantics: local admission is not
  downstream delivery acknowledgement or durability.

The internal application coordinator is named ApplicationLifetime to distinguish
application shutdown from the reusable Core component coordinator. Its behavior is
unchanged.

## Migration

This is a breaking authoring API change. Replace CreateInput and RunOnStart with
explicit native construction and FlowLifetime declarations. Input is a concrete
component property; Output and Events expose native BroadcastBlock instances.
Replace the former protected processing helper with ProcessMessageAsync, and supply
handlesErrors explicitly when needed. A finite flush sequence can use the shared
FlushMessagesAsync extension. Producers forward their existing IFlowSource members
to Lifetime. All bundled components, test probes, benchmarks and package consumer
examples have been migrated.

Current examples are in src/FluxFlow.Core/Nodes/README.md and docs/03-node-authoring.md.
No old factory or lifetime compatibility wrappers remain.

## Verification

The full Release suite passes 2,112 executions across .NET 8 and .NET 10, with no build
warnings. Coverage includes Batch size/time/final flush, tracing and error forwarding,
native ordering, ordinary-input drain, request/reply shutdown, output faults, source
stop phases and disposal. New regressions cover declarations with an already canceled
input and waiting for an active secondary input after the main input faults. Tests
which only exercised the removed base-class output factory were removed.

The public source-declaration baseline is updated for the reviewed breaking API.
Eight isolated package consumers pass on both .NET 8 and .NET 10: Core, Application,
Configuration, Routing, Batch, Scheduler, Resilience and RateLimit. These verify
compiled packages outside the repository rather than project references.
The final whitespace check is clean.
No commit or publication was performed.
