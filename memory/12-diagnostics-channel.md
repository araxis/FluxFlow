# Diagnostics Channel

Date: 2026-05-31

## Decision

Diagnostics are a separate operational channel.

They are not normal workflow output ports, and they are not domain events.

Use the channels like this:

- `StateChanges`: engine and workflow lifecycle.
- `Events`: workflow/domain activity.
- `Errors`: node processing failures.
- `Diagnostics`: operational health, status, counters, warnings, and metrics.

## Runtime API

- `FlowDiagnostic`
- `FlowDiagnosticLevel`
- `IFlowDiagnosticSource`
- `RuntimeFlowDiagnostic`
- `FlowApplicationHost.Diagnostics`
- `ApplicationRuntime.Diagnostics`
- `Workflow.Diagnostics`

`FlowNodeBase` implements `IFlowDiagnosticSource` by default and exposes:

- `TryEmitDiagnostic(...)`
- `EmitDiagnosticAsync(...)`

The runtime and workflow collectors enrich each diagnostic with:

- node address;
- node id;
- node type when known;
- node phase;
- original diagnostic payload.

## Notes

Diagnostics are side-channel observability. They should not affect workflow
completion or graph behavior.

Diagnostic streams use reliable fanout. Every linked subscriber receives every
diagnostic item, including slow subscribers with bounded input buffers.

`FlowApplicationHost.Diagnostics` is stable for the host lifetime. Callers can
link to it before `StartAsync`, so startup diagnostics are observable without a
race against runtime construction.

Component packages should define their own diagnostic names and attributes.
The engine owns collection, enrichment, and exposure.
