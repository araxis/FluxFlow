# FluxFlow.HttpTriggerSample

A runnable ASP.NET Core app that wires the standalone-node architecture end to end:

```
HTTP request ──MapFluxFlowTrigger──▶ RequestReplyBridge ──FlowMessage──▶ GreetingNode
     ▲                                       │  (correlate by CorrelationId)     │
     └──────── response ◀── HttpRequestContext ◀── bridge.Responses ◀────────────┘
```

The whole graph is composed by hand in `Program.cs` — `new` the bridge and the node,
`LinkTo` them, `MapFluxFlowTrigger`. No engine, no registry.

`GreetingNode` is a hand-written `FlowNode<HttpTriggerRequest, HttpTriggerReply>`: it
reads the request body as a name and replies, carrying the correlation id forward with
`With(...)`.

## Run

```bash
dotnet run --project samples/FluxFlow.HttpTriggerSample
# then, in another shell:
curl -d Ada http://localhost:5000/greet
# -> Hello, Ada! (correlation 3f2a…)
```

## Where an outbound call would go

To call an upstream service as part of answering, drop an `HttpClientNode` (from
`FluxFlow.Components.Http`) into the graph between the trigger and the reply: link
`bridge.Output` → a mapper that builds an `HttpRequestInput` → `HttpClientNode` →
a mapper that turns the `HttpResponseOutput` into an `HttpTriggerReply` →
`bridge.Responses`. Same envelope, same correlation id throughout.
