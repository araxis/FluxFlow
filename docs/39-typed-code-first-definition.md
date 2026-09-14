# Typed Code-First Application Definition

Use `ApplicationDefinitionBuilder` when an application needs the portable
`Resources` / `Workflows` model, typed links or Engine revisions. For direct Core
execution, construct `FlowApplication`, add node instances and link their ports.

## Workflow scopes

```csharp
var builder = new ApplicationDefinitionBuilder();
var orders = builder.AddWorkflow("Orders");
var audit = builder.AddWorkflow("Audit");
```

Workflow and component names form canonical addresses. Within JSON workflows,
local addresses can omit the workflow prefix. C# handles carry the full address.

## Built-in component methods

Component packages expose typed `AddX` extension methods, such as `AddWindow`,
`AddJsonStringify`, `AddJsonParse` and `AddMqttPublish`. These configure the existing
node factory and return typed handles. All 44 builtin AddX methods share their
callbacks with the 45 builtin DI registrations (including Gate).

```csharp
workflow.AddJsonStringify("encode", out var encode)
        .AddJsonParse("decode", out var decode);
encode.Output.LinkTo(decode.Input);
decode.Output.DiscardUnmatched();
encode.Events.DiscardUnmatched();
decode.Events.DiscardUnmatched();
var setup = builder.Build();
```

The fluent capture overload returns the workflow so calls can be chained. Direct
return overloads are equally valid. No intermediate graph object is created.

## Custom components

Use `workflow.AddComponent(name, type, factory, configure, createHandle)` to embed
a callback and return your handle. The factory constructs the node and declares
its blocks with `context.HasInput`, `HasOutput` and `HasEvents`. Configuration is
projected through `ComponentDefinitionBuilder`; a custom options class, if useful,
stays within the component package. See [Component registration](40-component-registration.md)
for the complete pattern. No descriptor, contract class or selector list is needed.

For a type already registered in DI, `workflow.AddComponent(name, type)` returns
a `ComponentHandle`. Its `Input<T>`, `Output<T>` and `SignalInput` methods create
`InputPortHandle<T>` and `OutputPortHandle<T>` address handles; actual port existence is checked during application construction.

## Connections and conditions

```csharp
source.Output.LinkTo(first.Input)
             .LinkTo(second.Input, value => value.Json.Deserialize<int>() > 10);
third.Input.LinkFrom(source.Output, "input > 20");
source.Output.OtherwiseTo(fallback.Input);
```

Use normal C# predicates for compiled definitions. String conditions require the
host's expression engine. Typed link helpers materialize the requested value from
the canonical message; links do not introduce a second transport format.

Direct port links support cross-workflow connections within the same application.
`workflow.Connect` restricts links to its workflow; `builder.Connect` supports
cross-workflow links. Cross-application handles, duplicate links and single-link
cardinality conflicts are rejected. Terminal signal feedback is supported;
ordinary message cycles are rejected. Unmatched outputs need a route, fallback,
request/reply receiver or an explicit `DiscardUnmatched()` policy.

## Resources

Application and workflow scopes can add resources and return typed resource
handles. Resource extensions retain their own `ApplicationResourceContract`
registrars. Use `definition.UseResource(propertyName, resource.Definition)` when
projecting a component's resource reference. Created revision resources are owned
by the revision; injected host services remain owned by the host.

## Build and execute

```csharp
await using var application = await setup.BuildApplicationAsync(serviceProvider);
await application.StartAsync();
```

For update hosting, pass `setup` to `services.AddFluxFlowUpdates(setup)` and obtain
`FluxFlowApplication` from the provider. `ApplicationPorts` accepts typed handles
for send, receive, observe and request/reply; address overloads remain available.

## Revisions, export and lifetime

`ApplicationSetup` contains the portable definition, C# links, resource registrars
and an immutable snapshot of component factories. Identical factory callbacks can
be shared with DI; conflicting callbacks for the same type fail before activation.
A changed callback affects the workflow revision. Failed configuration or handle
creation does not commit a partial component/factory entry.

Callbacks, predicates and live dependencies cannot be serialized. `setup.Definition`
is the portable model; serializing it intentionally excludes executable state.
`setup.ToDefinition()` rejects in-process factories/resources rather than silently
losing them. A JSON host supplies the required factories through DI. The independent
UI project can consume the portable model but does not belong to Core execution.
