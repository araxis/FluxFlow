# Typed Code-First Application Authoring

FluxFlow provides two independent, first-class ways to describe an application:

- portable JSON for files, configuration providers, remote configuration, hot reload, and UI/designer output;
- compiled C# for developers who want typed component handles, ordinary delegates, closures, and compiler-checked connections.

Both paths converge at link normalization, catalog validation, compilation, revision activation, and runtime routing. They do not converge through serialization. A code-first application builds and executes directly in memory; the C# builder has no JSON export or designer API.

## Workflow scopes

Use either of the two supported flat shapes:

```csharp
var application = new ApplicationDefinitionBuilder();
var main = application.AddWorkflow("main");
var audit = application.AddWorkflow("audit");
```

or fluent capture:

```csharp
var application = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var main)
    .AddWorkflow("audit", out var audit);
```

They are equivalent. There is intentionally no callback-style `AddWorkflow`: the workflow variables keep the graph flat and avoid nested callback trees.

## Complete component contracts

A `ComponentContract` is the single compiled-C# declaration of a component. It
owns the canonical type, runtime factory, native port and event bindings, typed
handle factory, and—when needed—an options-builder factory and apply delegate.
It builds one immutable `ComponentDescriptor`; it does not activate a node or
discover members through reflection.

Physical ports carry canonical `FlowMessage`; the typed handles below describe
caller-side values and predicates, not a second typed transport. `HasInput` and
`HasOutput` select native blocks, with optional `FlowValueShape` metadata on inputs.

```csharp
internal static class OrderComponents
{
    public static ComponentContract<OrderSourceOptions, OrderSourceHandle> Source { get; } =
        ComponentContract.Create(
            OrderComponentTypes.Source,
            runtime =>
            {
                runtime
                    .UseFactory(static context => new OrderSourceNode(
                        context.BindConfiguration<OrderSourceSettings>()))
                    .HasOutput(OrderComponentPorts.Output, static node => node.Output)
                    .HasEvents(OrderComponentPorts.Events, static node => node.Events);
            },
            static () => new OrderSourceOptions(),
            static (options, definition) => options.Apply(definition),
            static component => new OrderSourceHandle(component));
}

internal sealed class OrderSourceHandle(ComponentHandle component)
    : AuthoredComponentHandle(component)
{
    public OutputPortHandle<Order> Output { get; } =
        component.Output<Order>(OrderComponentPorts.Output);

    public OutputPortHandle<FlowValue> Events { get; } =
        component.Output<FlowValue>(OrderComponentPorts.Events);
}
```

The declaration is explicit: no reflection, scanning, generated code, global
registry, or convention lookup is involved. Component-specific option builders
retain their own validation and resource references. `UseFactory` executes only
during runtime activation; contract creation, application building, Designer
metadata collection, and JSON serialization do not create a node.

All official Composition packages expose the same concept through a `<Family>Components` class. For example, `HttpComponents.HttpRequest`, `FileSystemComponents.FileRead`, `SerializationComponents.JsonParse`, and `MqttComponents.MqttPublish` can be passed to `AddComponent`. The familiar `AddHttpRequest`, `AddFileRead`, and other `AddX` methods remain and delegate to those same contracts.

The raw `AddComponent(name, type, ...)`, `ComponentHandle.Input`, `Output`, and `SignalInput` APIs remain the dynamic escape hatch for plugin- or configuration-derived types. Normal code-first application code should prefer contracts and named handle properties.

## A complete graph

```csharp
var application = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var main)
    .AddWorkflow("audit", out var audit);

main
    .AddComponent(
        "source",
        OrderComponents.Source,
        options => options.Orders = LoadOrders(),
        out var source)
    .AddComponent("review", OrderComponents.Review, out var review)
    .AddComponent("priority", OrderComponents.Sink, out var priority)
    .AddComponent("standard", OrderComponents.Sink, out var standard);

audit.AddComponent("events", OrderComponents.EventCollector, out var events);

source.Output.LinkTo(review.Input);
review.Output
    .LinkTo(priority.Input, when: static order => order.Priority)
    .LinkTo(standard.Input, when: static order => !order.Priority);

review.Events.LinkTo(events.Input);
priority.Events.LinkTo(events.Input);
standard.Events.LinkTo(events.Input);

ApplicationSetup definition = application.Build();
services.AddFluxFlowUpdates(definition);
```

`Build()` freezes the builder and returns the directly hostable in-memory
setup, containing portable data separately from code links and trusted contracts.
It also captures the exact descriptors introduced by complete
contracts. Therefore `services.AddFluxFlowUpdates(definition)` is sufficient for the
normal code-first path; do not repeat the same component through the advanced
dynamic-registration surface.

Application dependencies remain ordinary DI registrations:

```csharp
services.AddSingleton(orderStore);
services.AddFluxFlowUpdates(definition);
```

A contract factory can resolve `orderStore` from
`ComponentActivationContext.Services`. Revision-owned keyed resources take
precedence, while ordinary host services remain host-owned.

Package-specific resource extensions use the same declaration-once rule.
`ApplicationResourceContract<THandle>` and
`ApplicationResourceContract<TOptions,THandle>` own the portable resource type,
typed handle, explicit option projection, and one package registrar. A
code-first definition captures the exact contracts it used, without activating
resources during authoring:

```csharp
var application = new ApplicationDefinitionBuilder()
    .AddResourceGroup("Messaging", out var messaging)
    .AddWorkflow("Orders", out var orders);

messaging.AddMqttBroker("Broker", options =>
{
    options.Host = "broker.internal";
    options.Port = 8883;
}, out var broker);

messaging.AddMqttClient("Client", options => options.Broker = broker, out var client);
orders.AddMqttPublish("Publish", options => options.Client = client, out var publish);

services.AddFluxFlowUpdates(application.Build());
```

This compiled-C# path does not repeat `.AddMqtt()`. The equivalent JSON path
must register `.AddMqtt()` because JSON never contains executable registrars.
Externally owned resources remain explicit keyed DI, using typed handles when
available: `services.AddExternalFluxFlowResource(client, controller)`.

For a JSON or low-level string definition, register complete contracts
explicitly because JSON contains no executable delegates:

```csharp
services
    .AddFluxFlowUpdates(jsonDefinition)
    .AddComponent(OrderComponents.Source)
    .AddComponent(OrderComponents.Sink);
```

`AddFluxFlowComponents().Advanced.AddDynamicComponent(type, configure)` remains
the explicit escape hatch for a dynamic descriptor that has no reusable
complete contract.

## Runtime operations keep the handles

Do not reconstruct addresses after building the graph. `ApplicationPorts`
accepts the same typed input, signal-input, and output handles:

```csharp
var receive = applicationHost.Ports.ReceiveAsync(priority.Output);
var sent = await applicationHost.Ports.SendAsync(
    review.Input,
    FlowMessage.Create(order));
var result = await receive;
```

Typed `ObserveAsync` and `SendAndReceiveAsync` overloads preserve exact payload
types. Durable input enqueue accepts `InputPortHandle<T>` and durable output
capture accepts `OutputPortHandle<T>`. Every typed overload delegates to the
existing canonical-address operation, retains timeout/cancellation/status
semantics, and keeps the complete `FlowMessage<T>` envelope explicit. String
and `ApplicationAddress` overloads remain for JSON, operations, and dynamic
selection.

## Connection scopes

Use the scope that makes ownership clearest:

```csharp
source.Output.LinkTo(sink.Input);       // concise; same owner, local or cross-workflow
sink.Input.LinkFrom(source.Output);     // equivalent route, declared at the input instead
main.Connect(source.Output, sink.Input);   // workflow-local only
application.Connect(source.Output, sink.Input); // explicit application scope
```

Choose one declaration per link; the first two lines above are alternatives.
Direct `LinkTo` returns the same output handle for fan-out. `LinkFrom`
returns the same input handle for fan-in, matching JSON input declarations:

```csharp
sink.Input.LinkFrom(first.Output).LinkFrom(second.Output);
```

Fan-in routes multiple sources to one input; it does not join their values or
guarantee ordering between sources. Both directions accept expression conditions
and typed predicates. A workflow-scoped `Connect` rejects a target from another
workflow. Direct port connections and application-scoped `Connect` allow
cross-workflow endpoints only within the same application builder.

Input and output generic types must match. Signal inputs are explicit payload-independent targets. Addresses remain fully qualified and stable, and all forms delegate to one validation and mutation operation. See [flat C#/JSON examples](50-flat-application-authoring.md).

## Conditions

An unconditional link needs no third argument. A portable expression can still be authored in C#:

```csharp
review.Output.LinkTo(priority.Input, condition: "input.Priority == true");
```

Expression text is compiled by the configured `IFlowExpressionEngine`, exactly like the portable JSON path.

For a compiled-only application, use a synchronous typed predicate:

```csharp
var threshold = 100m;
review.Output.LinkTo(priority.Input, when: order => order.Total >= threshold);
```

Typed predicates do not require an expression engine. They may be static or capture normal C# state. They run synchronously for each successful value message, so keep them fast, thread-safe, and free of surprising side effects. Async predicates, service resolution, retries, and policy execution are intentionally outside this API.

An error `FlowMessage<T>` never invokes a typed payload predicate and does not traverse that conditional route. Unconditional links continue to propagate error messages unchanged. Portable expression behavior remains unchanged and may inspect the existing expression context.

If a predicate throws, only that route is rejected. FluxFlow reports the condition failure with source and target context; other fan-out routes and later messages continue running.

## Revisions and lifetime

The built definition owns its component descriptors, application resource
contracts, factories, predicates, registrars, and captured closures. There is
no process-global registry. Engine combines host registrations and
definition-owned contracts into one effective component catalog and one
effective registrar set for each candidate revision. Reusing the same exact
descriptor/registrar identity is stable and deduplicated; conflicting contracts
for one type fail before activation.

A new descriptor identity used by a workflow marks that workflow updated. A
successful replacement retires the old graph and releases its factories and
predicates when the retired revision is no longer referenced. The update result
returned to its caller still reports `PreviousRevision`, while the application's
retained `LastUpdate` copy omits that retired snapshot. Failed planning or
activation leaves the previous revision active and usable.

## JSON and the designer

Portable JSON still has exactly the canonical `Resources` and `Workflows`
shape. `ApplicationDefinitionJson`, configuration sources, hot reload, and
Designer persistence continue to parse and emit that portable representation.
Setup-owned runtime descriptors and application resource contracts are
code-only and are never written to JSON. Deserializing a code-first definition's
portable projection therefore produces a JSON definition that again requires
explicit component and resource-family registration.

The UI/designer authors JSON. It does not load, edit, or export compiled C# builder definitions. Conversely, a C# builder result is not a serialization format: do not round-trip it through `ApplicationDefinitionJson` as part of startup, validation, revision planning, or execution.

Equivalent JSON and C# graphs should compile to equivalent runtime links, but code-only delegates and closures intentionally have no JSON representation.

## Relation to FluxFlow.Fluent

`ApplicationDefinitionBuilder` describes catalog-backed, hostable applications with resources, named workflows, stable addresses, revisions, configuration loading alternatives, and engine ports.

`FluxFlow.Fluent` builds from already constructed component instances with
`Flow.From(...).Then(...).To(...)` and uses the direct Core graph. It has no
Composition, JSON or hosting dependency. The optional hosting package owns
host integration. Arbitrary instances and delegates are not serialized.

Portable documents are in `FluxFlow.Json.Model`; link declaration metadata is
in `FluxFlow.Json.Links`. `ApplicationDefinition` has only Resources, Workflows
and ExpressionEngine. A code builder returns `ApplicationSetup`, whose Links,
ComponentDescriptors and ResourceContracts remain in-process. Hosting exposes
`CurrentSetup` for the full active setup and `CurrentDefinition` for its document
data. A checked `setup.ToDefinition()` projection includes declarative links and
rejects native delegates or activation contracts. It is not part of startup or
revision execution.
