# Logger setup

Register `services.AddLogger()` from `FluxFlow.Configuration.Components.Logging`
before `services.AddFluxFlowApplication(configuration)`.
This registers the `logger` component and the `logging.otlp` resource type.
Construction is deferred to application build; no collector connection is made
while registering or building an idle component.

```json
{
  "Resources": {
    "operations": {
      "Type": "logging.otlp",
      "Endpoint": "https://collector.example.com/v1/logs",
      "Protocol": "HttpProtobuf",
      "Headers": "Authorization=Bearer token,X-Tenant=factory",
      "ExportTimeout": "00:00:05",
      "ReportExportErrors": true
    }
  },
  "Workflows": {
    "production": {
      "prepareLog": {
        "Type": "mapper",
        "Engine": "jsonata",
        "Expression": "{'LogLevel':'Information','MessageTemplate':'Machine {Machine} measured {Value}','Args':[machine,value],'Attributes':{'messageId':$messageId}}",
        "Output": "writeLog.Input"
      },
      "writeLog": {
        "Type": "logger",
        "Logger": "operations"
      }
    }
  }
}
```

The mapper example also needs `AddMapper()` and `AddJsonataExpressionEngine()`.
For multiple logs, return an array of request objects from the expression.
The component accepts `Input`, `Output`, `Error` and `Events` link properties just
like other components. `Error` filters the existing output; it is not a physical port.

Omit `Logger` to use the application's `ILoggerFactory`. `AddLogger()` registers
standard .NET logging services without adding a provider, so configure the host's
providers normally. Set `Logger` to a resource reference to use that dedicated
factory instead. It never forwards those records to application logging; an unknown
reference fails application build. `Category` defaults to the component address.
`OutputCapacity` defaults to 128.

## OTLP resource

| Property | Behavior |
| --- | --- |
| `Endpoint` | Required absolute HTTP(S) URL. HTTP/protobuf uses the full logs URL, normally ending in `/v1/logs`; gRPC normally uses the collector origin. |
| `Protocol` | `HttpProtobuf` (default) or `Grpc`. |
| `Headers` | Standard OTLP comma-separated `name=value` headers. Values may contain `=`; encode commas as `%2C`. Keep credentials in protected configuration sources. |
| `ServiceName` | Optional. Omitted, null, empty or whitespace means an empty resource, with no default service or SDK attributes. A supplied name adds only `service.name`. |
| `ExportTimeout` | Native exporter timeout, default ten seconds; at least one millisecond. |
| `ReportExportErrors` | Default `false`: normal SDK background batching. `true`: wait for synchronous export and report unsuccessful export as a workflow error. |
| `MinimumLogLevel` | Default `Trace`. Intentionally filtered records are successful component operations without an export. |

Both modes use `ResourceBuilder.CreateEmpty()`. No SDK, environment or host resource
attributes are automatically added. Request attributes and logging scope attributes
are still included in individual records. With host tracing enabled for `FluxFlow.Core`,
the native logger captures the processing activity's trace and span IDs. Without an
activity listener, the component does not manufacture spans just for logging.

When error reporting is enabled, a success output follows the exporter's success
result. Collector rejection, network failure, timeout or suppressed export becomes
`FlowError`; later input remains processable. The SDK may not distinguish timeout
from other export failures, so the error does not assert a specific transport cause.
The timeout bounds exporter work, not input queueing or waiting for another call to
the same shared exporter. In-flight synchronous logging cannot be canceled through
`ILogger`; it finishes under the exporter's timeout.

With error reporting disabled, output means the log call completed; background
export failure does not retroactively change that output. DI owns and disposes the
dedicated factory, flushing the standard SDK batch on shutdown. Application loggers
follow their own host/provider settings and cannot gain per-call delivery reporting
from this component.

Exporter success is not a guarantee of collector persistence or downstream delivery.
The component adds no retry policy and does not duplicate SDK transport behavior.
