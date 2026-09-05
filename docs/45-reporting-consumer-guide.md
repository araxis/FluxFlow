# Consuming application events

Upgrading from the previous event contracts? See the
[canonical event migration](48-canonical-event-migration.md) before updating packages.

## Link once for the application's lifetime

Application event broadcast and `ApplicationPorts.ObserveAsync<T>` have different
delivery contracts. A passive port observer retires with an overflow fault when
its own bounded buffer fills; workflow output delivery continues independently.
Observe its `Completion` task to detect failure even while a consumer handler is
blocked. `ReadAllAsync` also propagates the fault when enumeration resumes.
Explicit disposal and normal completion drain queued messages successfully.
This does not make the application event broadcast a durable or lossless stream.

```csharp
using System.Threading.Tasks.Dataflow;
using FluxFlow.Engine;
using FluxFlow.Nodes;

var sink = new ActionBlock<FlowMessage>(
    message => Console.WriteLine(message.Headers.GetValueOrDefault(FlowEventHeaders.Type)),
    new ExecutionDataflowBlockOptions { BoundedCapacity = 256 });

using var link = application.Events.LinkTo(
    sink,
    new DataflowLinkOptions { PropagateCompletion = true },
    message => message.Headers.GetValueOrDefault(FlowEventHeaders.Kind) == "audit");

await application.StartAsync(cancellationToken);
// The same link observes subsequent revisions.
await application.StopAsync(cancellationToken);
await sink.Completion.WaitAsync(cancellationToken);
```

Resolve FluxFlowApplication normally. Accessing Events does not activate components. The source completes at application shutdown, including when no revision was activated. For early detachment, dispose the link, complete your target, then await its Completion. With PropagateCompletion=false, you must complete the target yourself. A link does not transfer ownership of your target to the library.

Configure through AddFluxFlow:

```csharp
options.Events = new()
{
    Application = "order-processing",
    Capacity = 256
};
```

Capacity must be between 1 and 65536. Consumer target bounds are independent. Keep predicates fast and side-effect-free; monitor target Completion for processing failures.

## Canonical, extensible events

```csharp
var message = new FlowEvent
{
    Name = "orders.accepted",
    Kind = FlowEventKind.Domain,
    Details = FluxFlow.Data.FlowValue.From(new
    {
        orderId = "order-42",
        quantity = 12,
        region = "north"
    })
}.ToMessage();
```

Publish through the component's existing event output. No ApplicationEvent subclass, report interface or global schema catalog is required. ToFlowEvent reads canonical event data; Details carries the producer's structured schema. Custom headers, identity and correlation survive aggregation.

Use FlowEventHeaders.Type/Kind/Level for common filtering. Source, Workflow, Component, Application and Revision provide context when known. Revision is the producing workflow revision, not a payload schema version. Consumers may establish their own event-name or schema-version convention without the aggregator interpreting it.

## Dashboards, metrics and reports

Native LinkTo predicates can select event types, categories, source addresses or payload values. A dynamic dashboard can compile its saved filter into a predicate in the consumer application. The library provides neither a filter language nor UI metadata catalog.

The consumer owns metric instruments, business calculations, report definitions, storage and historical queries. Existing runtime operational instrumentation is separate from consumer-specific metrics.

See the [historical reporting sample](../samples/FluxFlow.ReportingSample/README.md) for DI-selected reports, portable filters and a file archive. Those contracts live entirely in the sample, not in Engine.

## Reliability limits

BroadcastBlock is latest-value delivery. Slow targets can miss intermediate events, and bounded ingress can reject events without stopping the workflow. Linking an archive does not make the entire path durable; neither contiguous consumer sequence numbers nor zero locally observed errors prove complete runtime history.

For exact accounting or audit requirements, use a producer/persistence path with the required acknowledgements and durability. This feed alone cannot supply those guarantees. There is no replay or transactional reporting snapshot API.

Source completion does not mean every consumer has finished processing. Await your target separately. Events from concurrent components have no promised global causal order. Do not retain unbounded collections in long-lived consumers.
