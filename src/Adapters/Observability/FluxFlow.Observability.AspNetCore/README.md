# FluxFlow.Observability.AspNetCore

ASP.NET Core 10 live trace endpoint:

```csharp
builder.Services.AddFluxFlowTraceStream();
// After Build:
app.MapFluxFlowTraceStream("/fluxflow/traces").RequireAuthorization("Supervisor");
```

The host defines the Supervisor authorization policy. No authentication scheme is
installed by this package. The route is opt-in and can expose exception messages,
stack traces and span tags; authorize it as a diagnostic endpoint.

Each SSE event has type fluxflow.span and a JSON snapshot of a completed FluxFlow
span, including W3C IDs, parent, links, duration, status, tags and exception events.
Activity objects and workflow values are not queued. The stream covers this process,
not a cross-process trace database. It is live only: no replay or Last-Event-ID resume.
The queue is bounded per client (256 by default). If full, that client drains its
queued items and disconnects; reconnecting starts with new spans. Workflow execution
never waits for a client. Host disposal and request cancellation release listeners.

While connected, clients request full Activity data for live inspection, independently
of the SDK export sampler. No active clients means this listener requests no spans.
Use OTLP for durable collection and metrics/log export. This endpoint streams traces.
