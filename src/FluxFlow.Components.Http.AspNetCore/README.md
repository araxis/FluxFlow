# FluxFlow.Components.Http.AspNetCore

The ASP.NET Core HTTP **trigger** adapter for FluxFlow — and the only FluxFlow
package that references ASP.NET Core. It maps an endpoint's `HttpContext` onto a
`RequestReplyBridge<HttpTriggerRequest, HttpTriggerReply>` so an inbound request
flows into a dataflow graph and the correlated reply is written back to the caller.

```csharp
// compose: a request/reply bridge + your graph
var bridge = new RequestReplyBridge<HttpTriggerRequest, HttpTriggerReply>();
bridge.Output.LinkTo(handler.Input);     // your graph turns FlowMessage<HttpTriggerRequest>
handler.Output.LinkTo(bridge.Responses); // into FlowMessage<HttpTriggerReply> (same id)

// expose it as an endpoint
app.MapFluxFlowTrigger("/hook", bridge);
```

The endpoint:

1. builds an `HttpRequestContext` from the `HttpContext` (method, path, query, headers, body),
2. feeds it into the bridge (`bridge.Incoming`),
3. **holds the response open** until the graph posts the correlated reply — which the
   context writes to `HttpContext.Response`. If the bridge times the request out, the
   context writes `504`; an unexpected failure writes `500`; a bridge that's shutting
   down yields `503`.

Pass `correlationHeader` to seed the correlation id from an inbound trace/request id;
otherwise the bridge mints one. All the correlation machinery lives in
`FluxFlow.Components.RequestReply`; this package is just the `HttpContext` glue.
