# FluxFlow.Composition

Canonical application definitions, explicit component registration, addressing,
validation, link compilation, and code-first runtime ownership. The package is
Engine-independent.

## Application Shape

```json
{
  "Resources": {},
  "Workflows": {
    "Orders": {
      "Receive": { "Type": "source" },
      "Handle": {
        "Type": "handler",
        "Input": "Receive.Output"
      }
    }
  }
}
```

Resources, workflows, and components are named by object keys. Components are
flat; there are no maintained Composition, Nodes, or root Links wrappers.
Addresses are ordinal and case-sensitive. Links support fan-in, fan-out,
conditions, cross-workflow addresses, and explicit bounded signal feedback.
Ordinary data-processing cycles are rejected.

## Code-First Authoring

The canonical builders support either direct handle capture or chain-first
capture without introducing another model:

```csharp
var application = new ApplicationDefinitionBuilder()
    .AddResourceGroup("Messaging", out var messaging)
    .AddWorkflow("Orders", out var orders);

messaging.AddResource<object>("Client", "host.external", out var client);

orders
    .AddComponent("Source", "orders.source", out var source)
    .AddComponent("Sink", "orders.sink", out var sink)
    .Connect(
        source.Output<int>("Output"),
        sink.Input<int>("Input"));

var definition = application.Build();
```

Each fluent add delegates to the corresponding handle-returning method,
captures the committed typed handle, and returns the same parent builder.
Handles remain ordinary definition references. Component order does not imply
topology: links stay explicit, workflow links stay local, resource groups stay
resource-only, and `Build()` returns and freezes the same immutable
`ApplicationDefinition` used by JSON configuration.

`ApplicationLinkCompiler` owns parsing, address resolution, validation,
normalization, and deterministic ordering. Its result exposes executable
`Links` plus resolved `Declarations` for persistence. Serialize edited
`ApplicationLinkDeclarationProjection` values with
`ApplicationLinkCompiler.SerializeDeclarations(...)` so hosts and Designer use
the same exact `Port` / `Condition` grammar. Composition grants no production
friend access to Designer or Engine.

`ComponentDescriptor` declares one canonical type, typed
`FlowMessage<T>` ports, link cardinality, processing capabilities, and an
activation delegate. Author runtime-only descriptors through the flat
`AddRuntimeComponent(...)` callback.
DI builds one immutable `ComponentCatalog`; application validation, link
compilation, Engine activation, and Designer metadata all consume that catalog.
Errors travel on normal outputs. Application revisions own component and link
lifecycle but do not own external resources supplied by the host.

```csharp
services.AddFluxFlowComponents().AddRuntimeComponent(
    "orders.handle",
    component =>
    {
        component.UseFactory(CreateHandlerAsync);
        component.AddInput<Order>("Input");
        component.AddOutput<OrderResult>("Output");
    });
```

Composition adapters that materialize application resources implement
`IApplicationResourceRegistrar`. Its context exposes the complete definition,
revision identity, host services, and revision-owned `IServiceCollection`.
Canonical keyed DI helpers live in
`FluxFlow.Composition.DependencyInjection`; Engine consumes these low-level
contracts without making adapters depend on a hosting package.

Canonical workflow JSON selects an optional semantic `Processing` profile.
Composition maps that profile centrally to capacity, parallelism, and ordering.
Direct C# callers may still provide the technical options explicitly; those
compatibility settings are not primary workflow or Designer concepts.

`ApplicationRuntime` waits for all upstreams before completing a shared input,
faults fan-in once on the first upstream fault, and attempts all cleanup before
aggregating teardown failures.
