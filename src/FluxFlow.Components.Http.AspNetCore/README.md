# FluxFlow.Components.Http.AspNetCore

The ASP.NET Core HTTP **trigger** adapter for FluxFlow — and the only FluxFlow
package that references ASP.NET Core. An `HttpTriggerNode` is a component: it is given
its inbound request source (injected, keyed) and uses a
`RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply>` internally to correlate
the reply back to the caller. The endpoint just feeds the keyed source.

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

`AddFluxFlowHttpTrigger` registers a keyed request source + a keyed `HttpTriggerNode`
fed from it, and a hosted service that starts the trigger with the app (which wires the
graph and starts consuming) and disposes it on shutdown.
During stop the hosted lifetime completes the keyed request source before completing
the node, so late endpoint submissions are rejected instead of accepted into an
inactive trigger.
The registration validates the service collection, trigger name, and graph
configuration delegate. Trigger source capacity comes from `RequestReplyOptions`
and must be greater than zero.
`MapFluxFlowTrigger` validates the endpoint builder, route pattern, trigger name
or direct coordinator before handing the route to framework routing.

## Composition

This package does not expose `FluxFlow.Composition` node factories. It owns the
ASP.NET Core endpoint and trigger DI integration through `AddFluxFlowHttpTrigger`
and `MapFluxFlowTrigger`.

Use `FluxFlow.Components.Http.Composition` only for outbound `http.client`
composition. Config-composed inbound HTTP trigger factories are intentionally
outside this package boundary.

## Without DI

For tests or manual composition, hold a coordinator and map it directly:

```csharp
var coordinator = new RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply>();
coordinator.Output.LinkTo(handler.Input);
handler.Output.LinkTo(coordinator.Responses);
app.MapFluxFlowTrigger("/hook", coordinator);
```

## The endpoint

For each request it builds an `HttpRequestContext` from the `HttpContext` (method, path,
query, headers, body), feeds it to the trigger, and **holds the response open** until the
graph posts the correlated reply — which the context writes to `HttpContext.Response`. A
timed-out request writes `504`, an unexpected failure `500`, a shutting-down trigger `503`.

Pass `correlationHeader` to seed the correlation id from an inbound trace/request id;
otherwise the coordinator mints one. All the correlation machinery lives in
`FluxFlow.Components.RequestReply`; this package is just the `HttpContext` glue.
