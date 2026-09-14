# CallFunction: an ordinary request/reply component

Named function resources have been removed. There is no Resources.Functions,
FunctionDefinition or AddFunction/GetFunction registry. Old JSON declarations fail
with a migration message instead of being silently ignored.

CallFunction has standard Input, Output and Events ports. RequestTo names the
input that receives an inner request; ReplyFrom names the output whose correlated
reply it awaits. That reply is emitted through the component's own Output, which
can be linked normally. ReplyFrom is a remote source address, not another input
or an alias for the component's own output.

```json
{
  "Workflows": {
    "orders": {
      "call": {
        "Type": "CallFunction",
        "RequestTo": "processing.worker.Input",
        "ReplyFrom": "processing.worker.Output",
        "Timeout": "00:00:05",
        "Output": "next.Input"
      },
      "next": { "Type": "host.consume" }
    },
    "processing": {
      "worker": { "Type": "host.process" }
    }
  }
}
```

CallFunction is built into direct FlowJson.BuildAsync and Composition
BuildApplicationAsync. The host still registers its other component types.
Within a workflow, RequestTo and ReplyFrom may use component.port; between
workflows use workflow.component.port. Both use the existing ResolvePort rules.
Input/Output declarations and conditional links retain the normal flat JSON shape.

```csharp
await using var app = new FlowApplication();
var workflow = app.AddWorkflow("orders");
workflow.Add("worker", new MyProcessingComponent());
var call = workflow.Add("call", new CallFunction(
    "worker.Input", "worker.Output", TimeSpan.FromSeconds(5)));
var next = workflow.Add("next", new MyConsumerComponent());
call.Output.To(next.Input);
await app.StartAsync();
```

Timeout belongs to this calling component and includes admission plus reply waiting.
It is required; a positive duration or Timeout.InfiniteTimeSpan is accepted. This
does not add a timeout to a reusable function definition. The C# MakeFunction
helper and FlowFunction delegate remain: external callers can still bind two
ports and control waiting through their CancellationToken. RequestAsync remains.

The component uses the existing bounded FlowTransformNode and RequestAsync path.
Every inner request has its own correlation. The output restores the caller's
correlation, retains the reply headers, and records the reply message as causation.
Concurrent and nested requests therefore return to the correct caller. No payload
serialization is added between these ports.

Incoming FlowError messages bypass execution. Target errors are forwarded with
the caller's correlation; timeout/processing exceptions use the shared error and
diagnostic path. Original exceptions remain available through shared diagnostics.
A timeout stops waiting; it does not undo target work already admitted.

Validation resolves both addresses and rejects self/indirect cycles before startup.
Request dependencies participate in the existing completion ordering, so targets
stay alive while callers can use them. Application stop cancels pending exchanges;
there is no new queue, function registry or execution scheduler.

Revision-host execution/hot reload of this component is not implemented. The
built-in descriptor is installed only by the direct Composition build path.
Workflow connection activation is available for direct builds; see
[workflow connections](2026-09-13-workflow-connections.md). Optional resource privacy remains deferred.
