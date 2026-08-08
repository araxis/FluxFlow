# Unified Component Contracts

`ComponentContract` is the complete compiled-C# description of one FluxFlow
component. It removes the former split between a typed authoring declaration and
a separately registered runtime descriptor.

## Declare once

```csharp
public static ComponentContract<UppercaseHandle> Uppercase { get; } =
    ComponentContract.Create(
        "sample.uppercase",
        component =>
        {
            component
                .UseFactory(static _ => new UppercaseNode())
                .HasInput("Input", static node => node.Input)
                .HasOutput("Output", static node => node.Output)
                .HasEvents("Events", static node => node.Events);
        },
        static component => new UppercaseHandle(component));
```

The declaration creates and validates one immutable `ComponentDescriptor`.
`HasInput`, `HasOutput`, and `HasEvents` describe and bind existing node ports;
they do not create a second set of ports. The node factory is retained but is
not executed until Engine activates a workflow instance.

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

## Build and run directly

```csharp
var application = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var workflow);

workflow
    .AddComponent("source", SampleComponents.Source, out var source)
    .AddComponent("upper", SampleComponents.Uppercase, out var upper)
    .AddComponent("sink", SampleComponents.Sink, out var sink);

source.Output.ConnectTo(upper.Input);
upper.Output.ConnectTo(sink.Input);

var definition = application.Build();

var services = new ServiceCollection();
services.AddSingleton(resultCollector);
services.AddFluxFlow(definition);

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
    .AddFluxFlow(definition)
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
descriptors for JSON hosts and collect Designer metadata from the same source
without activating factories.
