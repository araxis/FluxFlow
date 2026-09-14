# FluxFlow.Observability

`FlowEventStream` provides optional, caller-owned observation of selected native
component event sources, independently of application construction:

```csharp
await using var events = new FlowEventStream(first.Events, second.Events);
using var subscription = events.OnEvent(value => Record(value));
```

Create it before application startup. Delivery remains bounded and best-effort;
slow observers may miss events and callback exceptions are isolated. `Events.LinkTo`
allows native observation. Dispose the stream separately; it never stops or owns
components. Completion follows all source completions, and source faults propagate
to stream completion. This does not replace OpenTelemetry tracing or reliable
business error routing. Direct component `Events.LinkTo` is also still available.

Source lifecycle faults are also available through the existing subscription.
The first failure recorded by FlowSource emits FluxFlow.Lifecycle.Exception and
logs EventId 2 (LifecycleException), with ComponentType, Operation and the original
exception. Operation is currently "source". The existing FluxFlow.Processing logger
category is retained; these entries carry no fabricated message identifiers.
Notification runs without inherited Activity context and does not create a span.
Source Completion remains faulted as before. Requested cancellation and successful
completion do not produce lifecycle failure logs. Cancellation-callback failures
also report as source.stop, preserving the caught aggregate. Failures in a source's
OnDisposeAsync hook report as source.dispose and are rethrown unchanged. Earlier
production faults are not logged again during disposal. Nested output/control
cleanup failures and other lifecycle owners are not yet covered.

Optional host-owned adapter from shared FluxFlow processing diagnostics to standard
Microsoft.Extensions.Logging. This is separate from the workflow's logger/counter
components and does not add logging dependencies to Core.

```csharp
using FluxFlow.Observability;

// loggerFactory is the host's configured ILoggerFactory.
using var diagnostics = FlowDiagnosticLogging.Subscribe(loggerFactory);
// Run the host/workflows while this subscription is alive.
```

Create one subscription per process, covering all FluxFlow applications in that
process. Multiple subscriptions intentionally produce multiple log deliveries.
The subscription discovers existing and future FluxFlow diagnostic listeners.
Dispose it at host shutdown; it does not dispose the factory. Already executing
callbacks can finish after disposal.

Processing failures are Error-level entries in category `FluxFlow.Processing`,
event ID 1 (`ProcessingException`). The original Exception is passed to ILogger,
including its inner exceptions and stack trace. Structured fields are ComponentType,
MessageId, CorrelationId and BusinessTraceId. BusinessTraceId is the workflow message
identifier, not the distributed Activity trace ID. Logging runs synchronously inside
the processing Activity when one exists, allowing the host's logging provider to
capture distributed trace/span context. Logging also works with tracing disabled.

The host chooses providers, filters and exporters, including any OpenTelemetry
logging provider. This package does not configure an SDK, exporter, endpoint or
telemetry backend. Provider failures are contained and not retried.

Coverage includes exceptions caught by FlowNode/FlowTransformNode's shared processing
boundary, Storage's shared parsing/operation error handling, and FileSystem read/write
parsing and classified operation failures, and Sessions recorder/query processing
and input parsing, plus Metrics aggregation and Projections processing exceptions.
Expectations reports malformed-input/evaluation exceptions only when they win its
single resolution; ignored late inputs and normal timeout outcomes do not report
exceptions. State reports caught operation failures, retaining reducer/key-selector
exceptions inside their domain wrapper. State also reports original conversion
exceptions retained by its materializer result. FlowError-only rejections remain
exception-free. MQTT publish/control also report retained materialization exceptions.
HTTP reports retained request-materialization exceptions at its rejection boundary.
Its shared failure path
reports caught input/send/timeout/body-read exceptions; status-only errors do not
manufacture exception reports. MQTT publish/control operation failures are reported
with original transport exceptions retained as InnerException when supplied by the
controller's failure factory. Background connection/trigger lifecycles are not covered.
Serialization's shared pipeline reports caught converter and content-materialization
failures with existing inner exceptions preserved; intentional encoding fallbacks
remain unchanged.
Mapping reports exceptions caught by its existing conversion/expression boundary.
Assertions reports input/evaluation exceptions; a predicate returning false remains
a normal result and does not create an exception log.
Validation reports exceptions handled by its selector/schema evaluation failure
path; ordinary schema-invalid results do not create exception logs.
Payloads reports content-materialization and handled inspection failures with existing inner exceptions;
unsuccessful format probes and decoded-preview fallbacks do not create exception logs.
The workflow FlowLoggerNode reports each caught attribute-selector exception
separately and preserves its single combined workflow error. Multiple exception logs
can therefore share one message ID; selector names remain in workflow error details.
Workflow FlowMetricsNode reports size-selection/general processing exceptions, and
FlowCounterNode reports input/predicate exceptions. Normal predicate rejection is
not an exception. These are workflow components, not runtime Meter instruments.
Delay, Throttle and Debounce report caught input-processing failures; queued Delay
and Debounce emissions establish their own message processing context before
reporting failures. Clock fallbacks and source/lifecycle failures remain separate.
Request/reply response conversion and reply-callback failures use the correlated
response's processing context. HTTP trigger reply conversion reports in that same
scope. Timeouts, caller-notification fallbacks and terminal faults retain their
existing treatment.
Window reports caught input failures before queued error emission. Correlation
reports after releasing its gate, retaining the first caught exception even if
subsequent timer scheduling also fails. Join reports message-backed command
failures once at its handling boundary; inner rethrows and source-less commands
do not generate additional processing reports.
Non-exception
outcomes such as Metrics group limits do not generate exception logs. Other component-local
catches, source/control/lifecycle failures, metrics and complete OpenTelemetry
integration remain subsequent work.
FlowError remains the serializable workflow output; this adapter does not serialize
or retain the exception itself.
