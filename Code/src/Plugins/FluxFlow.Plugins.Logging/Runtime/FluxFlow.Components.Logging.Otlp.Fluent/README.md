# OTLP logging resource

Use `AddOtlpLogger` to register a named collector for a workflow Logger. The compiled application includes the OpenTelemetry exporter when the workflow declares a collector. Nonempty header values are supplied through the generated environment variable.
