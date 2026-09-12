# Flat C#/JSON application authoring

Keep the same names and concepts in both representations: resources, workflows,
components, settings, and port connections. Use ordinary local variables or
fluent `out` captures instead of nested graph callbacks. Declaration order does
not wire components implicitly.

`target.Input.LinkFrom(source.Output)` mirrors JSON's
`"Input": "Source.Output"`. It returns that same input for flat fan-in.
`source.Output.LinkTo(target.Input)` remains the output-oriented alternative;
declare each link once, at whichever endpoint reads best.

These are two ways to author the same topology, not a C#-to-JSON exporter.
Compiled C# definitions execute directly in memory and capture their contracts.
JSON hosts register those contracts separately. Do not serialize a built C#
definition expecting its in-memory links, factories, or predicates to be saved.
Use the JSON authoring path for persisted, designer-editable applications.

## Pipeline

The first two examples assume application-owned `OrderComponents` contracts
with canonical types `orders.source`, `orders.review`, and `orders.sink`,
exposing compatible typed `Input`/`Output` properties. These are illustrative
contracts, not built-in processing operators. See
[component contracts](39-typed-code-first-authoring.md#complete-component-contracts)
for their declaration pattern.

```csharp
using FluxFlow.Composition.Authoring;

var application = new ApplicationDefinitionBuilder()
    .AddWorkflow("Main", out var main);

main
    .AddComponent("Source", OrderComponents.Source, out var source)
    .AddComponent("Review", OrderComponents.Review, out var review)
    .AddComponent("Sink", OrderComponents.Sink, out var sink);

review.Input.LinkFrom(source.Output);
sink.Input.LinkFrom(review.Output);

var definition = application.Build();
```

```json
{
  "Resources": {},
  "Workflows": {
    "Main": {
      "Source": { "Type": "orders.source" },
      "Review": { "Type": "orders.review", "Input": "Source.Output" },
      "Sink": { "Type": "orders.sink", "Input": "Review.Output" }
    }
  }
}
```

## Branching and fan-in

Declare sibling components at the same level. List each downstream connection
explicitly; no branch scope or special merge builder is needed.

```csharp
var application = new ApplicationDefinitionBuilder()
    .AddWorkflow("Main", out var main);

main
    .AddComponent("Source", OrderComponents.Source, out var source)
    .AddComponent("First", OrderComponents.Review, out var first)
    .AddComponent("Second", OrderComponents.Review, out var second)
    .AddComponent("Sink", OrderComponents.Sink, out var sink);

first.Input.LinkFrom(source.Output);
second.Input.LinkFrom(source.Output);
sink.Input.LinkFrom(first.Output).LinkFrom(second.Output);

var definition = application.Build();
```

```json
{
  "Resources": {},
  "Workflows": {
    "Main": {
      "Source": { "Type": "orders.source" },
      "First": { "Type": "orders.review", "Input": "Source.Output" },
      "Second": { "Type": "orders.review", "Input": "Source.Output" },
      "Sink": { "Type": "orders.sink", "Input": ["First.Output", "Second.Output"] }
    }
  }
}
```

Fan-in means independently receiving messages from both sources, not joining
paired values or imposing cross-source ordering. Conditional routing can use
`LinkFrom(source.Output, condition: "input.Priority == true")`, corresponding
to `"Input": { "Port": "Source.Output", "Condition": "input.Priority == true" }`.
Expression syntax belongs to the configured expression engine. A C# `when:`
predicate is a code-only alternative, not a portable condition.

## Resource-backed workflow

Package-local extensions use the same builder and connection primitives. Adding
a package-specific component does not require a central type switch or a second
graph model. Resource handles replace repeated address strings in C#.

```csharp
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Composition.Authoring;

var application = new ApplicationDefinitionBuilder()
    .AddResourceGroup("Messaging", out var messaging)
    .AddWorkflow("Main", out var main);

messaging
    .AddMqttBroker("Broker", options => options.Host = "broker.internal", out var broker)
    .AddMqttSubscription("Commands", options => options.TopicFilter = "commands/#", out var commands)
    .AddMqttClient("Client", options =>
    {
        options.ClientId = "relay";
        options.Broker = broker;
        options.AddSubscription(commands);
    }, out var client);

main
    .AddMqttReceive("Receive", options =>
    {
        options.Client = client;
        options.AddSubscription(commands);
    }, out var receive)
    .AddMqttPublish("Publish", options => options.Client = client, out var publish);

publish.Input.LinkFrom(receive.Output);
receive.Ack.LinkFrom(publish.Output);

var definition = application.Build();
```

```json
{
  "Resources": {
    "Messaging": {
      "Broker": { "Type": "mqtt.broker", "Host": "broker.internal" },
      "Commands": { "Type": "mqtt.subscription", "TopicFilter": "commands/#" },
      "Client": {
        "Type": "mqtt.client", "ClientId": "relay",
        "Broker": "Resources.Messaging.Broker",
        "Subscriptions": "Resources.Messaging.Commands"
      }
    }
  },
  "Workflows": {
    "Main": {
      "Receive": {
        "Type": "mqtt.receive", "Client": "Resources.Messaging.Client",
        "subscription": "Commands", "Ack": "Publish.Output"
      },
      "Publish": {
        "Type": "mqtt.publish", "Client": "Resources.Messaging.Client",
        "Input": "Receive.Output"
      }
    }
  }
}
```

This example describes the resource references and link topology, not an
end-to-end delivery guarantee. A production relay should explicitly choose its
acknowledgement/error policy and transform incoming data to its publishing
contract where needed. The `Ack` port is a payload-independent signal input;
ordinary input/output links require matching generic payload types.

`Build()` freezes the complete graph. Direct port links can cross workflows in
the same application; handles from different applications are rejected.
Existing cardinality, condition, cycle, and runtime validation remain in force.

The direct port methods are now `LinkTo` and `LinkFrom`. This is an intentional
source/binary breaking rename of `ConnectTo` and `ConnectFrom`, with no retained
aliases. Builder-level `Connect` is unchanged. Conditions and runtime behavior
are unchanged. The public API baseline is updated; package versions and previously
published artifacts are unchanged. Include this in a newly versioned package
when preparing the next release and rebuild consumers against it.

The [runnable composition sample](../samples/FluxFlow.CompositionSample/Program.cs)
demonstrates both condition forms:

```csharp
source.Output.LinkTo(upper.Input, when: static value => !string.IsNullOrWhiteSpace(value.Deserialize<string>()));
sink.Input.LinkFrom(upper.Output, condition: "input != 'SKIP'");
```

The sample host registers the existing expression adapter for `condition:`.
Optional named engines and JSON condition parameters are described in
[configurable conditions](51-expression-conditions.md). Run the sample with
`--json` to exercise host configuration and a persisted workflow override.
The first link rejects empty values with a C# predicate; the second rejects
`SKIP` with an expression. Only `ALPHA` and `BETA` reach the sink. The expression
adapter is a sample-host dependency, not a new Composition or Engine dependency.

For a runnable host example without an external broker, see
[the composition sample](../samples/FluxFlow.MqttCompositionSample/Program.cs).
