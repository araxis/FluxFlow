# FluxFlow.Components.Http.AspNetCore

The ASP.NET Core HTTP **trigger** adapter for FluxFlow — and the only FluxFlow
package that references ASP.NET Core. An `HttpTriggerNode` is a directly constructed
component with one bounded `Incoming` target. Its request/reply bridge uses Core's
exchange matching to return graph replies to callers. The endpoint feeds the
keyed node directly; there is no separate registered source buffer.

## DI composition (recommended)

```csharp
builder.Services.AddFluxFlowHttpTrigger("greet", trigger =>
{
    // wire your graph: trigger.Output (requests) -> ... -> trigger.Responses (replies)
    var greeting = new GreetingNode();
    trigger.Output.LinkTo(greeting.Input);
    greeting.Output.LinkTo(trigger.Responses);
});

var app = builder.Build();
app.MapFluxFlowTrigger("/greet", "greet");   // endpoint feeds the keyed trigger by name
```

`AddFluxFlowHttpTrigger` registers a keyed `HttpTriggerNode` and a hosted service
that starts it with the app and disposes it on shutdown. Stop closes its actual
bounded input and awaits caller notifications and cleanup. Late submissions are
rejected. Canceling the host's stop wait requests a fault; already-running caller
callbacks remain owned by node Completion and disposal.
The registration validates the service collection, trigger name, and graph
configuration delegate. Trigger input capacity comes from `RequestReplyOptions`
and must be greater than zero.
`MapFluxFlowTrigger` validates the endpoint builder, route pattern, trigger name
or direct coordinator before handing the route to framework routing.
Trigger names are trimmed before keyed DI registration and endpoint lookup, so
configuration-bound names with surrounding whitespace resolve to the same
logical trigger.

## Composition

This package does not expose `FluxFlow.Composition` node factories. It owns the
ASP.NET Core endpoint and trigger DI integration through `AddFluxFlowHttpTrigger`
and `MapFluxFlowTrigger`; the host owns the ASP.NET Core app, route selection,
request body policy, and host-owned trigger lifetime registration.

Use `FluxFlow.Components.Http.Composition` only for outbound `http.request`
composition. Config-composed inbound HTTP trigger factories are intentionally
outside this package boundary.

## Without DI

For tests or manual composition, hold a coordinator and map it directly:

```csharp
var coordinator = new RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply>();
coordinator.Output.LinkTo(handler.Input);
handler.Output.LinkTo(coordinator.Responses);
await coordinator.StartAsync();
app.MapFluxFlowTrigger("/hook", coordinator);
```

`new HttpTriggerNode(options)` can also be passed directly to
`workflow.Add(name, trigger)`. Output and Responses are the shared bridge's
canonical blocks; no request/response conversion queues are inserted. Wire a
handler output to the returned component's `Inputs["Responses"]` control endpoint.
The application starts the trigger and keeps Responses open while business work
drains. Submit transport contexts to `trigger.Incoming`; if a host explicitly
links an external source to it, that source remains host-owned. The former
`HttpTriggerSource` class and source-taking constructor are removed. Typed reply validation and rejection diagnostics remain
at the trigger boundary. Manual standalone use requires explicit `StartAsync()`;
the keyed hosted registration starts it automatically.

## The endpoint

`HttpRequestContext` captures the current server operation's `ActivityContext`
before queueing. Distinct concurrent HTTP requests keep their own trace parent,
sampling flag and trace state in both request/reply and fire-and-forget mode.
An absent context stays parentless rather than inheriting a worker's activity.
The platform handles HTTP wire propagation; the host owns instrumentation and export.

For each request it builds an `HttpRequestContext` from the `HttpContext` (method, path,
query, headers, body), feeds it to the trigger, and **holds the response open** until the
graph posts the correlated reply — which the context writes to `HttpContext.Response`. A
timed-out request writes `504`. An already-admitted request failed during shutdown
or by an unexpected error writes `500`; a newly rejected submission writes `503`.

Pass `correlationHeader` to retain an inbound ID on the transport context. Core
still mints a unique graph exchange correlation, so reused header values cannot
cross-match responses. Build replies from the graph request envelope. This
package owns the HTTP context adaptation, not a separate exchange implementation.
