# Canonical event migration

## Decision and scope

Runtime event streams use one transport contract: `ISourceBlock<FlowMessage>`.
The envelope owns message identity, timing, tracing, correlation and headers.
An event payload describes what happened. `FlowEvent` is a convenient creation
and access model, not a second transport contract or a base class for every event.
Custom event details belong to their producer; adding an event does not require
an application-wide registry, an event subclass or a reporting service.

This is an intentional breaking release. No parallel legacy event stream,
callback subscription registry or forwarding compatibility package is added.

| Package | Published comparison | New candidate |
| --- | --- | --- |
| FluxFlow.Nodes | 4.0.0 | 5.0.0-rc.1 |
| FluxFlow.Composition | 7.0.0-rc.1 | 8.0.0-rc.1 |
| FluxFlow.Engine | 8.0.0-rc.1 | 9.0.0-rc.1 |

The [coordinated package train](49-canonical-package-release-train.md) records
dependent candidates, published comparisons and dependency waves. Do not mix
previously compiled components with these new contracts without validating them.

## API replacements

| Previous contract | Replacement |
| --- | --- |
| FlowNode.Events and FlowSource.Events carrying FlowEvent | The same port names carrying FlowMessage |
| Event bindings accepting ISourceBlock<FlowEvent> | Event bindings accepting ISourceBlock<FlowMessage> |
| ComponentEventSource, ComponentInstance and ComponentPorts event signatures | Canonical FlowMessage sources; recompile factories and registrations |
| Typed authoring handles' Events properties | Recompile against the canonical event port handle types |
| ApplicationRuntime.Events | ApplicationRuntime.Activity |
| ApplicationPorts.SystemEvents and Diagnostics with typed Signals payloads | The same port names carrying canonical FlowMessage events |
| ApplicationDiagnostic / ApplicationSystemEvent and their supporting Signals types | Event type/kind/level metadata and structured event details |
| ComponentEvent wrapper | Canonical message event data and source context |

For application-lifetime aggregation, link to `FluxFlowApplication.Events`.
Use `ApplicationRuntime.Activity` only when deliberately observing that runtime's
lifetime. The application feed remains linked across workflow revisions.

## Consumer steps

1. Update the complete affected package dependency set and rebuild custom nodes,
   registrations and consumers. Old compiled binaries are not supported merely
   because their type or member names still look the same.
2. Replace `ActionBlock<FlowEvent>` observation targets with
   `ActionBlock<FlowMessage>`. Use `TryGetFlowEvent` or `ToFlowEvent` when the
   payload fields are needed; keep the original message for identity and context.
3. Read event type/kind/level from `FlowEventHeaders` and event-specific data from
   the producer's documented payload. Do not equate an old enum's numeric value
   with the replacement event name. Review filters for the actual emitted names.
4. Preserve the distinction between a diagnostic event describing a failure and
   a workflow error message. Event severity does not mean the envelope is an error.
5. Keep dashboard filters, metric calculations, report definitions and archives
   in the consumer. Use ordinary Dataflow links and predicates.

For example, given an existing application:

```csharp
var target = new ActionBlock<FlowMessage>(message =>
{
    if (!message.TryGetFlowEvent(out var observed) || observed is null)
        return;

    Console.WriteLine($"{observed.Name}: {message.CorrelationId}");
}, new ExecutionDataflowBlockOptions { BoundedCapacity = 256 });

using var link = application.Events.LinkTo(target,
    new DataflowLinkOptions { PropagateCompletion = true },
    message => message.Headers.GetValueOrDefault(FlowEventHeaders.Kind) == "diagnostic");
```

Use the `FluxFlow.Nodes` and `System.Threading.Tasks.Dataflow` namespaces.
After application shutdown, await the target's completion. For early detachment,
dispose the link, complete the target and await it separately.

The feed is best-effort, not a durable audit log. Bounded ingress may reject an
event, and a slow target can miss intermediate observations. Revision metadata
identifies the producing workflow revision, not a payload schema version.
See the [consumer guide](45-reporting-consumer-guide.md) for delivery and ownership.

## Compatibility review

Package-local comparison exceptions enumerate the retired or changed public
signatures for this major migration only. The comparison still uses the real
published baseline and checks both supported target frameworks. Do not treat an
exception as an implementation adapter or as proof that old consumers still run.

Before publication, validate a fresh package-only consumer using the complete
new dependency set, then migrate the client application and test its workflows,
event filters, revision changes, reporting ingestion and shutdown. Source builds
alone do not establish compatibility with previously published dependent binaries.
