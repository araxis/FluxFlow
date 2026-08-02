# Definitions And Links

The canonical vNext definition is
`FluxFlow.Composition.Model.ApplicationDefinition`. It is an immutable
application document with exactly two case-sensitive root objects:
`Resources` and `Workflows`.

## Canonical Shape

```json
{
  "Resources": {
    "Messaging": {
      "Broker1": {
        "Type": "sample.broker",
        "Host": "localhost"
      },
      "Client1": {
        "Type": "sample.client",
        "Broker": "Resources.Messaging.Broker1"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Source": {
        "Type": "sample.source",
        "Count": 3
      },
      "Sink": {
        "Type": "sample.sink",
        "Input": "Source.Output"
      }
    }
  }
}
```

The document rules are deliberately narrow:

- both root sections are required and no other root property is allowed
- workflows and resource groups are objects keyed by exact names
- workflow objects contain components directly
- resource groups omit `Type`; resource leaves require a string `Type`
- components require a string `Type`
- component and resource settings are direct properties
- `Configuration`, per-component `Resources`, `Nodes`, and `Links` wrappers are
  not part of the canonical shape
- names use ordinal, case-sensitive comparison and cannot contain dots or
  surrounding whitespace
- `Resources` and `System` are reserved workflow names; `Type` is reserved in
  resource maps

The model copies caller-owned collections into immutable ordinal dictionaries
and clones retained `JsonElement` values. Mutating an input dictionary or
disposing its source `JsonDocument` cannot change a built definition.

## Model Types

```csharp
using FluxFlow.Composition.Model;

var application = new ApplicationDefinition(
    resources:
    [
        new("Messaging", new ResourceGroupDefinition(
        [
            new("Broker1", new ResourceInstanceDefinition("sample.broker"))
        ]))
    ],
    workflows:
    [
        new("Orders", new WorkflowDefinition(
        [
            new("Source", new ComponentDefinition("sample.source"))
        ]))
    ]);
```

`ResourceDefinition` is a closed resource shape with
`ResourceGroupDefinition` and `ResourceInstanceDefinition` variants. Groups
hold child resources; instances hold `Type` and flat properties.

## C# Authoring

`FluxFlow.Composition.Authoring.ApplicationDefinitionBuilder` is the canonical
code-first authoring surface. It builds the same immutable model as JSON while
preserving the document's application, resource-group, resource, workflow, and
component structure:

```csharp
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Composition.Authoring;

var application = new ApplicationDefinitionBuilder();

var messaging = application.AddResourceGroup("Messaging");
var broker = messaging.AddMqttBroker("Broker1", options =>
{
    options.Host = "localhost";
    options.Port = 1883;
});
var commands = messaging.AddMqttSubscription("Commands", options =>
{
    options.TopicFilter = "orders/commands";
    options.Qos = MqttQos.AtLeastOnce;
});
var client = messaging.AddMqttClient("Client1", options =>
{
    options.ClientId = "orders";
    options.Broker = broker;
    options.AddSubscription(commands);
});

var orders = application.AddWorkflow("Orders");
var receive = orders.AddMqttReceive("Receive", options =>
{
    options.Client = client;
    options.AddSubscription(commands);
});
var handle = orders.AddComponent("Handle", "orders.handle");
var publish = orders.AddMqttPublish("Publish", options =>
{
    options.Client = client;
    options.MaximumPendingRequests = 64;
});

orders.Connect(
    receive.Output,
    handle.Input<MqttReceivedApplicationMessage>("Input"));
orders.Connect(
    handle.Output<MqttPublishMessage>("Output"),
    publish.Input);

ApplicationDefinition definition = application.Build();
```

The same declarations can use fluent capture when several siblings belong to
one scope. Every fluent `Add*` overload appends its `out` handle last and
returns the exact parent builder instance:

```csharp
var application = new ApplicationDefinitionBuilder()
    .AddResourceGroup("Messaging", out var messaging)
    .AddWorkflow("Orders", out var orders);

messaging
    .AddMqttBroker(
        "Broker1",
        options =>
        {
            options.Host = "localhost";
            options.Port = 1883;
        },
        out var broker)
    .AddMqttSubscription(
        "Commands",
        options =>
        {
            options.TopicFilter = "orders/commands";
            options.Qos = MqttQos.AtLeastOnce;
        },
        out var commands)
    .AddMqttClient(
        "Client1",
        options =>
        {
            options.ClientId = "orders";
            options.Broker = broker;
            options.AddSubscription(commands);
        },
        out var client);

orders
    .AddComponent("Handle", "orders.handle", out var handle)
    .AddMqttPublish(
        "Publish",
        options =>
        {
            options.Client = client;
            options.MaximumPendingRequests = 64;
        },
        out var publish)
    .Connect(
        handle.Output<MqttPublishMessage>("Output"),
        publish.Input);

ApplicationDefinition fluentDefinition = application.Build();
```

The `out var` form keeps the chain on its structural parent while exposing the
ordinary typed definition handle for later settings and explicit links. The
handle-returning form remains fully supported; both forms delegate to the same
add operation and build the same immutable `ApplicationDefinition`.
Declaration order never creates topology, selects a default port, or crosses a
resource/workflow boundary. `workflow.Connect(...)` remains workflow-local;
use `application.Connect(...)` only for an intentional cross-workflow link.

Official composition packages expose one flat
`Add{Component}(name, Action<{Component}Builder>)` callback per component.
Each component keeps its own strongly typed settings builder because component
behavior is not uniform. Typed resource and port handles provide references
without manually copying address strings. The lower-level `AddResource`,
`AddComponent`, `Set`, and `UseResource` APIs remain the explicit escape hatch
for application-owned component types.

The authoring boundary has these rules:

- callbacks are one level deep; adding a resource or component commits it
  atomically after the callback succeeds
- component settings remain direct JSON properties; the API adds no hidden
  `Options`, `Configuration`, `Resources`, or `Links` wrapper
- use `Set` for JSON configuration values and `UseResource` for handles;
  passing a handle to `Set` is rejected
- workflow `Connect` creates local links only; use `application.Connect` for an
  intentional cross-workflow link
- typed connections require the same message type, while signal inputs remain
  explicit typed control endpoints
- calling `Build()` returns `ApplicationDefinition` and freezes the complete
  authoring graph; later mutations are rejected
- handles are definition references, not resolved runtime services, and do not
  change resource ownership or lifecycle

The resulting definition uses the existing JSON serializer, configuration
loader, link compiler, validation, and runtime. The builder is an authoring
front end, not a second executable model:

```csharp
var canonicalJson = ApplicationDefinitionJson.Serialize(application.Build());
```

## JSON And Configuration

`ApplicationDefinitionJson` is the authoritative strict reader and
deterministic writer:

```csharp
var definition = ApplicationDefinitionJson.Deserialize(json);
var canonicalJson = ApplicationDefinitionJson.Serialize(definition);
```

Writing always emits `Resources` before `Workflows`, sorts resource, workflow,
component, and property names ordinally, and recursively sorts nested JSON
object properties. Array order remains unchanged. Duplicate JSON properties
are rejected, including duplicates inside retained option values.

Engine's `ConfigurationApplicationDefinitionSource` can load the canonical
model from an `IConfiguration` root or an explicitly named host section:

```csharp
using FluxFlow.Engine;

var rootDefinition = await new ConfigurationApplicationDefinitionSource(
        configuration)
    .LoadAsync();

var hostedDefinition = await new ConfigurationApplicationDefinitionSource(
        configuration,
        "Application")
    .LoadAsync();
```

Configuration providers flatten JSON and cannot retain every lexical detail.
Use `ApplicationDefinitionJson` when exact JSON shape and duplicate-property
detection are required at the source boundary.

## Address Rules

`FluxFlow.Composition.Addressing.ApplicationAddress` is the shared ordinal,
case-sensitive address value.

| Target | Form | Example |
|---|---|---|
| Nested resource | `Resources.Group.Resource` | `Resources.Messaging.Client1` |
| Absolute workflow port | `Workflow.Component.Port` | `Orders.Source.Output` |
| Local workflow port | `Component.Port` | `Source.Output` |
| System events | reserved absolute address | `System.Events.Output` |
| System diagnostics | reserved absolute address | `System.Diagnostics.Output` |

Local references require a workflow context:

```csharp
var input = ApplicationAddress.ResolvePort("Sink.Input", "Orders");
var output = ApplicationAddress.Parse("Orders.Source.Output");
var resource = ApplicationAddress.Parse("Resources.Messaging.Client1");
```

Addresses reject blank segments, surrounding whitespace, ambiguous resource
references used as ports, and unrecognized `System` paths. Equality and hashing
are ordinal, so `Orders.Source.Output` and `orders.Source.Output` are distinct.

## Canonical Link Compilation

Port properties use the registered port name and may declare one link, or an
array of links, on either endpoint:

```json
{
  "Type": "sample.sink",
  "Input": [
    "Source.Output",
    {
      "Port": "Other.Source.Output",
      "Condition": "value != null"
    }
  ]
}
```

The compiler accepts a string, an object with exact `Port` and optional
`Condition` property names, or a mixed array of those forms. An empty array
means no links. A link must appear on only one endpoint.

```csharp
using FluxFlow.Composition.Links;

var catalog = provider.GetRequiredService<ComponentCatalog>();
var result = new ApplicationLinkCompiler(catalog, expressionEngine)
    .Compile(definition);

if (!result.IsValid)
{
    foreach (var diagnostic in result.Diagnostics)
        Console.Error.WriteLine(diagnostic);
}
```

`ComponentCatalog` descriptor metadata determines whether a property is an
input or output. The compiler converts local references to absolute addresses,
preserves `ApplicationLinkDeclarationSide`, and sorts successful links by
source and target. Ordinary component settings that do not match a registered
port remain settings and are ignored by the link compiler.

The same pass exposes complete, structurally resolved declaration properties
through `ApplicationLinkCompilationResult.Declarations`. Each
`ApplicationLinkDeclarationProjection` contains the source, target, declaration
side and location, normalized port reference, and optional condition text.
Designer persistence consumes those facts and calls
`ApplicationLinkCompiler.SerializeDeclarations(...)`; it does not parse or
reconstruct the link grammar independently. A partially malformed declaration
array is not projected, so the raw property remains available for lossless
round-trip and diagnostics.

Validation rejects malformed declarations, unknown component types, missing
components or ports, exact payload-type mismatches, duplicate endpoint pairs,
explicit single-link claim conflicts, condition compilation failures, and
data-link cycles. Multiple upstreams to one input and multiple targets from one
output remain valid by default. Use `CompositionPortLinkCardinality.Single`
only for a port whose contract is exclusive.

Cycle validation is port-aware. A link targeting metadata registered with
`CompositionPortKind.Signal` is a bounded feedback relation and is excluded
from the unbounded data-cycle graph. This permits relations such as
`Receive.Output -> Handle.Input` and `Handle.Output -> Receive.Ack`. Merely
naming an ordinary message port `Ack`, `Nak`, or `Cancel` does not make it a
signal, so data cycles cannot bypass validation through port naming. Local and
fully qualified addresses use the same classification.

Each distinct condition string is compiled once per compiler invocation using
`IFlowExpressionEngine`. A compiled link exposes `IsMatch(...)` and
`TryMatch(...)`; the latter returns a captured evaluation exception so the
future runtime can reject only that link for that message and continue with
sibling links.

Reserved system streams require host-supplied
`ApplicationSystemOutputMetadata`. That keeps system payload contracts in the
Engine while allowing Composition to perform the same exact type check without
depending on Engine. `FluxFlow.Engine.Hosting` activates successful compiled
links through the stable-port runtime.

## Legacy Document Migration

Composition no longer loads or executes the earlier `workflows` / `nodes` /
`links` model and ships no legacy converter. Convert old documents outside the
runtime, review any lossy configuration/resource flattening manually, persist
canonical JSON, then validate it with `ApplicationDefinitionJson` and
`ApplicationLinkCompiler`.

## Legacy Engine Documents

Engine no longer exposes a second executable definition model or converter.
Executable resource nodes require a manual host-owned resource mapping;
non-default phases require a semantic processing profile. Persist only the
canonical Composition document after those decisions are made.

Next: [Node Authoring](03-node-authoring.md).
