# Observability and live supervision

The runtime exposes standard .NET ActivitySource and Meter instruments named
FluxFlow.Core. It does not depend on the OpenTelemetry SDK. Exception diagnostics
preserve the original Exception outside workflow data.

## Register a host

Add FluxFlow.Observability.OpenTelemetry and, when exporting OTLP,
OpenTelemetry.Exporter.OpenTelemetryProtocol. Register telemetry before flow hosted
services so it starts before the graph and stops after the graph drains.

```csharp
using FluxFlow.Observability;
using OpenTelemetry;
using OpenTelemetry.Resources;

builder.Services.AddFluxFlowTelemetry()
    .ConfigureResource(resource => resource.AddService("orders"))
    .UseOtlpExporter();
// Register the application's flow hosted services after telemetry.
```

The same host owns SDK disposal, flush, logging providers and resources. Repeated
AddFluxFlowTelemetry registration does not add multiple diagnostic hosted services.
Use one diagnostic subscription per process: do not also manually subscribe
FlowDiagnosticLogging in the same host.

The adapter flushes pending logs during hosted-service stop, with a five-second
bound, before host services are disposed. This allows an OTLP exporter to create
its HTTP client even when a short-lived host has not previously exported a batch.
Keep the registration before flow hosted services so their final diagnostics are
included. Exporter failures do not interrupt shutdown; this is not a delivery
guarantee when a backend is unavailable.

Configure standard OTEL_EXPORTER_OTLP_ENDPOINT, OTEL_EXPORTER_OTLP_PROTOCOL and
OTEL_EXPORTER_OTLP_HEADERS in the deployment. For example, a local Collector HTTP
receiver can use http://localhost:4318 and http/protobuf. A Datadog Agent must have
its OTLP receiver enabled for the signals you send; this code does not configure
an Agent or embed a Datadog API key. The SDK's exporters own batching and transport.
Actual remote Collector/Datadog acceptance requires deployment validation.

The sample also uses the official OpenTelemetry.Instrumentation.AspNetCore and
OpenTelemetry.Instrumentation.Http packages to capture ingress/outbound HTTP spans
and metrics. Add those to web hosts that need transport instrumentation.

## Signals and their meaning

| Signal | Behavior |
| --- | --- |
| Traces | Existing flow.process/flow.control/flow.request Activities retain W3C context. Aggregate scopes link their inputs. Sampled processing failures include standard exception events with type, message and full stack/inner exception text. |
| Logs | Processing failures use EventId 1 and lifecycle failures EventId 2 in category FluxFlow.Processing. ILogger receives the original Exception; processing logs correlate with the active span. Lifecycle logs suppress unrelated ambient Activity context. |
| Metrics | fluxflow.processing.completed counts completed processing scopes; fluxflow.processing.active tracks their balance; fluxflow.processing.duration records seconds; fluxflow.exceptions counts observed exception reports. |

Metrics operate without trace sampling. Component type is the processing dimension;
exception metrics additionally use stable operation and exception type. Message IDs,
correlation IDs, keys, payloads and URLs are not metric dimensions. Processing scopes
include queued work, controls and aggregates, so these are not unique-message counts.
Multiple distinct caught failures can belong to one message. Register providers
before starting workflows to observe the full lifetime of active operations.

Core does not allocate a timing scope when no processing instruments are enabled.
Metric and diagnostic callbacks are contained so observer failures cannot fault
processing. This does not guarantee delivery through a broken observer/exporter.

## Live SSE supervision (ASP.NET Core 10)

Add FluxFlow.Observability.AspNetCore:

```csharp
builder.Services.AddFluxFlowTraceStream();
// After builder.Build(), apply the host's existing authentication/authorization:
app.MapFluxFlowTraceStream("/fluxflow/traces")
    .RequireAuthorization("Supervisor");
```

The host defines the Supervisor policy. This opt-in endpoint exposes diagnostic
details, including exception text, and should use the host's access controls.
The sample restricts it to direct loopback connections.

Each event has type fluxflow.span and a JSON snapshot of a completed span: trace,
span and parent IDs, name/kind, timing, status/description, trace state/flags, tags,
links and events. Tag values are represented as strings. Workflow payload objects
and live Activity/Exception objects are not queued.

This is a live process-wide feed, not a trace database. There is no history,
Last-Event-ID replay or cross-process stitching. Clients each have a bounded queue
(default 256). A full queue disconnects that client after its queued events drain;
it must reconnect, and events lost during that interval are not replayed. Slow
clients never block processing. Request cancellation and host disposal release
subscriptions. Connected clients request full Activity data for live inspection;
when no clients are connected this listener requests no spans. Durable traces,
logs and metrics belong in the configured OTLP backend.

## Runnable sample

```powershell
dotnet run --project samples/FluxFlow.HttpTriggerSample -- --urls http://127.0.0.1:5088
# Another terminal:
curl.exe -N http://127.0.0.1:5088/fluxflow/traces
# Another terminal:
curl.exe -d Ada http://127.0.0.1:5088/greet
```

The sample exports OTLP only when OTEL_EXPORTER_OTLP_ENDPOINT is explicitly set.
Without it, SSE and local diagnostic logging work without a collector.

## Lifecycle review and verification limits

Shared sources and processing-node lifetime owners report first terminal faults,
stop-callback failures and disposal-hook failures. Join and Retry report at their
owned terminal boundaries; RequestReply reports deduplicated final failures. A
source-less Join command uses lifecycle diagnostics rather than invented message
identifiers. Normal timeout/NAK/exhaustion outcomes remain domain results. Existing
cleanup rethrows of an already-owned fault are not logged repeatedly.

The tests verify both target frameworks where supported, SDK in-memory export,
real OTLP HTTP/protobuf request generation against an in-process transport, trace
sampling independence, parent/link regressions, lifecycle failure containment,
bounded SSE queues and actual ASP.NET Core SSE framing/disconnect cleanup. A live
loopback sample request and SSE span were also verified.

A fresh package-only consumer additionally verified telemetry and error recovery
on .NET 8 and .NET 10, with the packaged SSE endpoint registered and its host
started on .NET 10. Reproduce it with
`pwsh -NoProfile -File eng/verify-observability-consumer.ps1`.
The [consumer guide](../eng/observability-consumer/README.md) describes the isolated
feed/cache and verification record. This is a focused four-package check; it does
not qualify the entire release feed. The SSE package requires .NET 10; release
tooling's default .NET 8 consumer target is not suitable for that package.

This is not an assertion that every catch in every third-party client produces a
FluxFlow event. Hosts still own external resources and exporters. Live Datadog/
remote Collector acceptance, deployment authentication/proxy settings and published-feed
qualification are not exercised here. The separate Sessions design concern remains
deferred; these changes do not resolve it.

A real local Collector 0.160.0 acceptance run now verifies OTLP HTTP/protobuf
delivery of all three signals, W3C parent continuity, concurrent fanout, exception
log/span correlation and full details, final shutdown processing, metric totals,
and an external SSE client's matching span identities and disconnect cleanup.
See the [repeatable Collector check](../eng/observability-collector/README.md).
This uses a dedicated source-referenced net10 host; it does not extend the prior
package-only qualification to a real Collector or verify Datadog.

Sources: [OpenTelemetry .NET host/exporter documentation](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md)
and [ASP.NET Core 10 ServerSentEvents API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.results.serversentevents?view=aspnetcore-10.0).
