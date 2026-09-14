# FluxFlow.Observability.OpenTelemetry

Call services.AddFluxFlowTelemetry() before registering flow hosted services.
It registers FluxFlow.Core ActivitySource/Meter with the standard OpenTelemetry SDK
and subscribes diagnostic logging for the host lifetime. Core has no SDK dependency.
Pending logs are flushed during hosted-service stop, before DI disposal, with a
five-second bound. Register before flow hosted services so their final logs are
included. Exporter failures do not interrupt shutdown.

Configure service identity with ConfigureResource and exporters on the returned
OpenTelemetryBuilder. For OTLP add OpenTelemetry.Exporter.OpenTelemetryProtocol
and call UseOtlpExporter(); standard OTEL_EXPORTER_OTLP_* settings select endpoint,
protocol and headers. This supports an OpenTelemetry Collector or a Datadog Agent
configured to receive OTLP. No credentials or remote destinations are built in.

Metrics: fluxflow.processing.completed (operations), fluxflow.processing.active
(operations), fluxflow.processing.duration (seconds), fluxflow.exceptions
(observed exceptions). Processing scopes include control, queued and aggregate
work, so they are not unique message counts. Exceptions count reporting boundaries,
not failed-message totals. Tags use component type and, for exceptions, operation
and exception type; no message IDs, keys or payload values are metric dimensions.
Metrics operate independently of Activity sampling.

Traces retain existing W3C parent/link behavior. Caught processing exceptions attach
standard exception events to sampled Activities, including message and stack/inner
exception text. Logs retain original Exception objects. Configure exporter-side
redaction if needed. Lifecycle logs do not fabricate spans or message identifiers.
