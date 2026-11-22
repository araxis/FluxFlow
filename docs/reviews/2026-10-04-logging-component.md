# Logging component and OTLP resource

The new component owns ordinary native dataflow blocks and borrows an `ILogger`.
It accepts a `LogRequest` or JSON array, preserves structured template arguments
and scope attributes, and returns a result for each request. Per-item parsing or
logging failures use existing FlowError conversion and do not stop subsequent items.
No Core or Application runtime changes were needed.

Configuration owns destination selection. AddLogger registers the component and
logging.otlp resource factories in native DI. An omitted Logger selects the host
factory; an explicit resource selects only that factory. Missing references fail
build. DI owns disposal. There is no new logging registry, transport client or
OpenTelemetry dependency in the runtime component package.

Dedicated resources always start with ResourceBuilder.CreateEmpty. A nonempty
ServiceName adds only service.name; absence, null and whitespace add nothing. Native
log record attributes, scopes and activity context remain available. HTTP/protobuf
and gRPC use the standard OTLP exporter, with configured headers and export timeout.

Ordinary mode uses SDK batching. Optional ReportExportErrors uses a small factory
adapter because ILogger.Log returns void and SDK processors suppress export failures.
The adapter keeps native SDK record creation, pooling and exporter ownership, records
the synchronous ExportResult for the current call, and raises a caller-visible
failure after the native log call returns. It never throws through the processor or
retains a pooled record. Concurrent calls serialize access to the exporter's reused
buffer. The component's ordinary error handling then produces FlowError.

Export timeout covers exporter work, not queue wait or serialization behind another
call. It does not cancel a synchronous application ILogger provider. Export success
does not promise collector persistence. No custom retry or persistence layer was added.

## Verification

- Final full Release suite: 2,664 passing test executions, zero warnings.

- Logger tests: 38 cases on each of .NET 8 and .NET 10. They cover typed/JSON input,
  arrays and partial failure, native formatting, structured values, exception identity,
  lineage, independent failures, filtering and borrowed logger ownership.
- Local HTTP/2 gRPC and HTTP/protobuf collectors verify real wire records, headers,
  explicit/empty resource attributes, native trace/span IDs with an activity listener,
  synchronous response waiting, timeout reporting and collector failures.
- Integration covers JSONata array output, duplicate link declarations, application
  fallback, dedicated destination isolation, concurrent exporter calls, missing
  resources and atomic invalid configuration rejection.
- The solution, package manifest, dependency plan, public API baseline and isolated
  Configuration consumer include the new module.
- Isolated package consumer passed on .NET 8/10 using candidate version
  0.0.0-local.20261004201713. It validates packaged setup, native port resolution and
  structured LogRequest serialization. No package was published.

Tests use local unencrypted collectors, not external authenticated TLS services.
TLS and exporter protocol behavior remain delegated to the native exporter.
