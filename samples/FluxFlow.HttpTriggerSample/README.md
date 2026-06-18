# FluxFlow.HttpTriggerSample

A runnable ASP.NET Core app that wires the standalone-node architecture end to end:

```
HTTP request ──MapFluxFlowTrigger("/greet","greet")──▶ HttpTriggerNode ──FlowMessage──▶ GreetingNode
     ▲                                                       │ (RequestReplyCoordinator)     │
     └──────── response ◀── HttpRequestContext ◀── trigger.Responses ◀──────────────────────┘
```

The trigger is registered as a component in DI and its graph is wired in the
`AddFluxFlowHttpTrigger` callback — no engine, no registry. The endpoint
(`MapFluxFlowTrigger("/greet", "greet")`) just feeds the keyed trigger by name; the
trigger's `RequestReplyCoordinator` correlates the reply back to the caller.

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

To call an upstream service while answering, drop an `HttpClientNode` (from
`FluxFlow.Components.Http`) into the graph between the trigger and the reply: link
`trigger.Output` → a mapper that builds an `HttpRequestInput` → `HttpClientNode` →
a mapper that turns the `HttpResponseOutput` into an `HttpTriggerReply` →
`trigger.Responses`. Same envelope, same correlation id throughout.
