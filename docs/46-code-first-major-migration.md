# Code-First Major Migration

This guide covers the breaking authoring changes in the code-first
simplification prerelease. It is for applications and component packages moving
from the previous Composition, Engine, Fluent, and durability lines.

Portable JSON remains supported. The major change is the compiled C# authoring
surface and the removal of duplicate declarations and registrations.

## Choose the authoring path first

Use typed C# when the application is compiled with its workflow definition and
should use C# types, variables, predicates, factories, and dependency injection.
Use portable JSON when the definition must be stored, designed, edited, loaded,
or hot-reloaded as data.

The paths converge on the same Engine, but they do not serialize into each
other. A C# definition may own executable delegates and contract instances; a
JSON definition never does.

## Application registration

The normal code-first host no longer repeats every runtime component after the
definition is built.

Before:

```csharp
var definition = definitionBuilder.Build();

services.AddFluxFlowComponents()
    .AddRuntimeComponent("sample.uppercase", component =>
    {
        component
            .UseFactory(static _ => new UppercaseNode())
            .HasInput("Input", static node => node.Input)
            .HasOutput("Output", static node => node.Output)
            .HasEvents("Events", static node => node.Events);
    });

services.AddFluxFlow(definition);
```

After:

```csharp
var application = new ApplicationDefinitionBuilder();
application.AddWorkflow("main", out var workflow);

workflow.AddComponent(
    "upper",
    SampleComponents.Uppercase,
    out var upper);

var definition = application.Build();
services.AddFluxFlow(definition);
```

`SampleComponents.Uppercase` is a complete `ComponentContract`. It owns the
portable component type, runtime factory, typed bindings, and typed handle.
`ApplicationDefinition` retains the selected contract, so
`AddFluxFlow(definition)` is the only normal host registration.

## Component package declarations

Replace split descriptor/authoring declarations with one package-owned complete
contract. The runtime binding vocabulary describes existing node members:

- `HasInput`, not `AddInput`;
- `HasSignalInput`, not `AddSignalInput`;
- `HasOutput`, not `AddOutput`; and
- `HasEvents`, not `AddEvents` or an implicit global event port.

Events are explicit named output ports. A component handle should expose them as
`OutputPortHandle<ComponentEvent>` when the component supports events.

`ComponentAuthoringContract` has been replaced by `ComponentContract`.
Consumers should select the package's exported complete contract rather than
constructing a second runtime registration.

## Typed application links

Replace string targets such as `"sink.Input"` with captured handles:

```csharp
workflow
    .AddComponent("source", SampleComponents.Source, out var source)
    .AddComponent("upper", SampleComponents.Uppercase, out var upper)
    .AddComponent("sink", SampleComponents.Sink, out var sink);

source.Output.ConnectTo(upper.Input);
workflow.Connect(upper.Output, sink.Input);
```

Use `application.Connect(...)` for an explicit cross-workflow connection. A C#
condition may be supplied directly:

```csharp
upper.Output.ConnectTo(
    sink.Input,
    when: static value => value.Length > 0);
```

Portable expression strings remain available for JSON-compatible conditions.

## Typed runtime and durability operations

Keep the handles captured during authoring. `ApplicationPorts` accepts typed
input, signal-input, and output handles for send, receive, observe, and
request/reply operations. Durable input enqueue and durable output capture also
accept the corresponding typed handles.

These overloads use the same canonical addresses and runtime implementation;
they do not create a second routing or durability system.

## Application resources

Code-first resources use package-owned `ApplicationResourceContract` values.
The definition retains their explicit registrars and the Engine owns their
revision lifetime. A code-first MQTT client, for example, does not require a
second host-side MQTT registrar call.

JSON cannot carry executable registrars. A JSON host must continue to register
the package behavior that may be named in its data.

## JSON applications

The JSON path remains explicit:

```csharp
var definition = JsonSerializer.Deserialize<ApplicationDefinition>(json)!;

services
    .AddFluxFlowComponents()
    .AddSources();

services.AddFluxFlow(definition);
```

Keep explicit family registration here. It is the trusted catalog boundary for
portable type names and does not represent duplicate code-first registration.
JSON persistence and hot reload remain data-only and never embed CLR delegates,
component contracts, resource contracts, or service providers.

## Advanced dynamic registration

Raw runtime registration is an escape hatch for dynamic catalogs and low-level
integration packages. Move normal direct calls to the explicit advanced
surface:

```csharp
services
    .AddFluxFlowComponents()
    .Advanced
    .AddDynamicComponent("sample.dynamic", component =>
    {
        component
            .UseFactory(static _ => new DynamicNode())
            .HasInput("Input", static node => node.Input)
            .HasOutput("Output", static node => node.Output)
            .HasEvents("Events", static node => node.Events);
    });
```

Do not use this path merely to make a normal code-first definition executable.

## Fluent applications

`FluxFlow.Fluent` is now a concise facade over a canonical
`ApplicationDefinition` and `FluxFlowApplication`. Continue using the fluent
syntax, but host and observe the canonical definition/application exposed by the
graph. Replace `FlowGraph.Runtime` with `FlowGraph.Definition` or
`FlowGraph.Application` according to whether the caller needs the immutable
blueprint or the running Engine application. There is no separate Fluent
runtime lifecycle.

## Readiness

Application readiness is optional. Add the health package and call its standard
registration only when a host needs a readiness check. The adapter observes
existing application state; it adds no worker, polling, storage query, or
endpoint by itself.

## Migration checklist

1. Upgrade the affected package closure to one consistent prerelease wave.
2. Replace `ComponentAuthoringContract` with package-owned complete
   `ComponentContract` values.
3. Rename declaration mappings from `Add*` to `Has*` and declare Events
   explicitly.
4. Build applications with captured typed handles and typed connections.
5. Remove repeated code-first component and resource registration.
6. Keep explicit package registration for JSON and dynamic catalogs.
7. Move raw registrations to `Advanced.AddDynamicComponent`.
8. Use typed runtime and durability overloads where the authoring handle is
   available.
9. Update Fluent hosting to the canonical graph definition/application.
10. Run unchanged, rejected-reload, retained-route, shutdown, and durability
    recovery tests before promoting from the prerelease.
