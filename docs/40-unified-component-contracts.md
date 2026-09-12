# Unified Component Contracts

`ComponentContract` belongs to optional definition-based application integration.
Direct Core and fluent applications construct component instances normally and do
not require a contract or factory. In this optional layer, a contract keeps typed
authoring and its runtime descriptor in one declaration.

## Declare once

```csharp
public static ComponentContract<UppercaseHandle> Uppercase { get; } =
    ComponentContract.Create(
        "sample.uppercase",
        component =>
        {
            component
                .UseFactory(static _ => new UppercaseNode())
                .HasStandardPorts();
        },
        static component => new UppercaseHandle(component));
```

The declaration creates and validates one immutable `ComponentDescriptor`.
`HasStandardPorts` applies to an `IFlowComponent` with exactly `Input`, `Output`
and `Events`, and no control ports. It uses `FlowPorts.InputName`, `OutputName`
and `EventsName` and binds the component's existing native blocks. `FlowNode`
already declares this view; a custom wrapper can use
`FlowPorts.CreateStandard(input, output, events)` without transferring ownership.

An optional input shape can be supplied with
`HasStandardPorts(FlowValueShape.String("Text"))`. Omitting it leaves structural
payload validation unspecified. Schema inspection runs neither the constructor
nor the input materializer. Construction happens only when the definition is
activated, either into the direct Core application or an optional update host.

Use explicit `HasInput`, `HasOutput`, `HasEvents` and `HasSignalInput` bindings
for source/sink components, extra or renamed ports, terminal controls, or custom
link cardinalities. The standard shortcut rejects those shapes at activation;
it never silently drops ports. These bindings name existing blocks, not a second
set of queues. Direct C# applications still construct instances normally and need
none of this optional registration.

An options-aware contract adds only the component-specific authoring boundary:

```csharp
public static ComponentContract<SourceBuilder, SourceHandle> Source { get; } =
    ComponentContract.Create(
        "sample.source",
        component => component
            .UseFactory(static context => new SourceNode(
                context.BindConfiguration<SourceSettings>()))
            .HasOutput("Output", static node => node.Output)
            .HasEvents("Events", static node => node.Events),
        static () => new SourceBuilder(),
        static (options, definition) => options.Apply(definition),
        static component => new SourceHandle(component));
```

## Build and run a definition

```csharp
var application = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var workflow);

workflow
    .AddComponent("source", SampleComponents.Source, out var source)
    .AddComponent("upper", SampleComponents.Uppercase, out var upper)
    .AddComponent("sink", SampleComponents.Sink, out var sink);

source.Output.LinkTo(upper.Input);
upper.Output.LinkTo(sink.Input);

var definition = application.Build();

var services = new ServiceCollection();
services.AddSingleton(resultCollector);
services.AddFluxFlowUpdates(definition);

await using var provider = services.BuildServiceProvider();
await provider.StartFluxFlowApplicationAsync();
```

The definition owns the exact descriptors introduced by its contracts. Engine
merges them with host-registered descriptors once per candidate revision and
uses that effective catalog for validation, links, port surfaces, and
activation. Registering the same descriptor reference in both places is safe;
two different descriptors with the same type are rejected.

Ordinary application dependencies remain ordinary DI services. During
activation, revision-owned keyed resources are resolved first and host services
are the explicit fallback. Engine disposes only its revision-owned provider; it
never takes ownership of the host provider.

## JSON stays independent

Canonical JSON still contains exactly `Resources` and `Workflows`. It never
contains factories, selectors, handles, delegates, or definition-owned
descriptors. A JSON host therefore registers its executable contracts
explicitly:

```csharp
var definition = ApplicationDefinitionJson.Deserialize(json);

services
    .AddFluxFlowUpdates(definition)
    .AddComponent(SampleComponents.Uppercase)
    .AddComponent(SampleComponents.Sink);
```

Serializing a code-first definition produces only its portable projection.
Deserializing that projection returns a JSON definition and intentionally does
not recreate executable C# behavior.

## Advanced dynamic registration

Keep the low-level path when a reusable typed contract is not the source of the
component, such as a dynamic plugin or externally selected type:

```csharp
services.AddFluxFlowComponents()
    .Advanced
    .AddDynamicComponent("dynamic.transform", component =>
    {
        component
            .UseFactory(CreateTransform)
            .HasInput("Input", static node => node.Input)
            .HasOutput("Output", static node => node.Output);
    });
```

Do not use this as a second registration for a contract already added to a
code-first definition.

## Revisions and ownership

Descriptor reference identity is the explicit identity of executable component
behavior. Reusing one contract is revision-stable. Replacing it with a new
descriptor marks affected workflows updated without inspecting or hashing
delegate bodies. A failed candidate leaves the current revision active. A
successful candidate retires and releases the old component generation and its
captured factory state.

Official component packages expose their complete declarations through
`<Family>Components`. Their family registration extensions register those exact
descriptors for JSON hosts. Every ordinary family provides presentation through
a separate optional Designer package: call its `Add<Family>Design` extension after
registering the runtime family. These projections neither activate factories nor
resolve host resources. See the [package registration table](14-public-api-overview.md).

Metadata-only edits keep presentation flat without a component builder:

```csharp
registration.AddDesignMetadata("sample.uppercase", metadata => metadata
    .WithDisplay("Uppercase", "Text")
    .WithPort("Input", PortDirection.Input, summary: "Text to convert"));
```

Only declared options/resources/ports can be decorated. Structural fields remain
owned by the original descriptor; a missing presentation target fails explicitly.
