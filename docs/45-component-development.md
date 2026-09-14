# Current component development

Use the current source checkout and coordinated package versions. Earlier migration
documents describe intermediate APIs; this guide describes the current runtime.
The selected phase 1 refactoring checkpoint is validated; phase 2 observability
has started in small steps.

## Runtime contract

Every physical input/output carries the non-generic `FlowMessage`. A message contains
either a `FlowValue` or a serializable `FlowError`. `FlowValue` wraps immutable JSON;
passing it between components does not require serializing it again. Deserialize
only the domain input needed by the receiving component, and serialize a newly
produced domain result when creating its output value.

Typed `FlowMessage<T>` helpers may still appear in definition/Engine-facing APIs.
They do not change the physical runtime port contract. `FlowContent` preserves exact
bytes and content metadata inside domain request/response objects.

## Choose an existing execution shape

For one input value producing one result value, use `FlowValueTransformNode`:

```csharp
using FluxFlow.Data;
using FluxFlow.Nodes;

public sealed class UppercaseNode : FlowValueTransformNode
{
    protected override ValueTask<FlowValue> ProcessAsync(
        FlowValue value, CancellationToken cancellationToken)
        => ValueTask.FromResult(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value.Json.Deserialize<string>()!.ToUpperInvariant(), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
}
```

This is the same pattern exercised by `samples/FluxFlow.DirectSample`. It delegates
envelope creation, errors, ordering and bounded output delivery to the existing
transform runtime. It is not a universal base for every component shape.

Use `FlowNode` for envelope-aware processing, conditional/multiple emissions or
completion work. A handler can keep the original envelope and a local typed request:

```csharp
protected override async Task ProcessAsync(FlowMessage message)
{
    var input = message.Value!.Json.Deserialize<string>()!;
    var result = input.ToUpperInvariant();
    await EmitAsync(message.With(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))))), Stopping);
}
```

`message.With(...)` creates a derived result with lineage; `message.WithError(error)`
creates error data. Components can inspect correlation, headers and other envelope
metadata directly. No intermediate typed envelope or custom context is required.
The package template demonstrates domain-specific parsing failures, diagnostics,
clock injection and event ordering.

## Conversion and failures

Keep System.Text.Json options and domain deserialization in the component or its
small family-local pipeline. A private converter is suitable for text/binary/JSON
body normalization, as in HTTP and MQTT. Source-generated serializer metadata remains
a planned optimization; this sequence has not implemented it across the components.

Ordinary processing exceptions become error messages through the existing node
boundary. Use local catches when preserving a component-owned error code/category
and details. Default FlowNode behavior forwards incoming errors without executing
business logic; override it only when the component intentionally handles errors.
Explicit Fault and infrastructure failures can still terminate a node. Do not
claim the runtime can survive every possible failure.

Existing diagnostics remain supported. Full standard OpenTelemetry logging,
tracing and metrics integration is phase 2; host/SSE integration is phase 3.
The first phase 2 addition exposes original processing exceptions through an
in-process listener at the shared node boundary. See
[observability progress](plans/2026-09-12-observability.md) for its exact coverage.
Hosts can now connect these events to their existing ILoggerFactory with
`using var diagnostics = FlowDiagnosticLogging.Subscribe(loggerFactory);` from the
optional FluxFlow.Observability package. Keep one subscription alive per process;
see its [README](../src/Adapters/Observability/FluxFlow.Observability/README.md).

## Construction and optional Composition

Construct components and add them directly with
`workflow.Add("name", component)`. Link their registered endpoints and run the
owning `FlowApplication`. Component-local pipelines can share parsing and lifetime
work without introducing another public base class.

Composition is optional. Register a node factory with services.AddComponent(type, factory).
Declare actual blocks using context.HasInput/HasOutput/HasEvents, with explicit names
and link limits where needed. No input-shape
schema is registered. HTTP, MQTT and State retain their public custom-materializer
extension points; these implement `Materialize(FlowValue)` without descriptive metadata.

## Runnable references

- `samples/FluxFlow.DirectSample`: direct graphs, value transforms, routing and sinks.
- `samples/FluxFlow.ComponentPackageTemplate`: a reusable envelope-aware component.
- `samples/FluxFlow.CompositionSample`: optional registration and definition hosting.
- `samples/FluxFlow.HttpTriggerSample`: host integration; running it starts a server.

The independent `C:/Projects/FluxFlow.UI` project consumes packages; UI code does
not belong in the FluxFlow runtime. Updating a local checkout does not automatically
rebuild the package feed consumed by that project.
