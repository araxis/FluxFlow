# End-To-End Code-First Simplification

FluxFlow's compiled-C# path is one declaration-to-execution path. A component
factory constructs the node and declares its ports, an application builder places it and
returns a typed handle, and that same handle is used for links, host interaction,
durability, and explicit resource binding.

## The normal path

```csharp
var builder = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var workflow);

workflow
    .AddUppercase("first", out var first)
    .AddUppercase("second", out var second);

first.Output.LinkTo(second.Input);

var definition = builder.Build();

var services = new ServiceCollection();
services.AddFluxFlowUpdates(definition);

await using var provider = services.BuildServiceProvider();
var application = provider.GetRequiredService<FluxFlowApplication>();
await application.StartAsync();

var receive = application.Ports.ReceiveAsync(second.Output);
await application.Ports.SendAsync(first.Input, FlowMessage.Create("hello"));
var result = await receive;
```

There is no second component registration. ApplicationSetup embeds the callback;
Engine combines it with host callbacks and validates the created instances.

## Resources follow the same rule

Package resource extensions add `ApplicationResourceContract` values. A
contract contains one portable type, an explicit option projection, a typed
handle, and the exact registrar that executes that resource family. It does not
contain a live client, service provider, global registry, or serialized
delegate.

Code-first MQTT therefore needs only its normal host dependencies and the
built definition:

```csharp
messaging.AddMqttBroker("Broker", configureBroker, out var broker);
messaging.AddMqttClient("Client", options => options.Broker = broker, out var client);
orders.AddMqttPublish("Publish", options => options.Client = client, out var publish);

services.AddSingleton<IMqttTransportFactory>(transportFactory);
services.AddFluxFlowUpdates(builder.Build());
```

The package registrar is embedded as runtime-only definition state. Concrete
controllers created for a revision remain revision-owned; host-supplied
transports, secrets, clocks, and externally bound controllers remain host-owned.

## C# and JSON stay independent

| Source | Portable model | Executable contracts | Host registration |
|---|---|---|---|
| Compiled C# | `Resources` and `Workflows` | Component callbacks and resource registrars captured by the builder | Ordinary host dependencies only |
| JSON/configuration | `Resources` and `Workflows` | None | Explicit component/resource family registration |

Canonical JSON never contains factories, registrars, handles, CLR types,
predicates, or node instances. Serializing a code-first definition produces
only its portable projection. Deserializing it creates the JSON path again and
therefore requires explicit family registration such as `.AddMqtt()`.

## Typed handles at every boundary

`ApplicationPorts` accepts typed handles for send, signal send, receive,
observe, and request/reply. `DurableApplicationInputs` accepts
`InputPortHandle<T>`, and durable output capture accepts
`OutputPortHandle<T>`. Typed keyed-resource helpers accept
`ResourceHandle<T>`. Each overload validates null, then delegates to the
existing canonical address implementation; there is no second behavior and no
payload-only message shortcut.

String and `ApplicationAddress` overloads remain important for JSON,
operational tools, remote control, and dynamically selected ports.

## Direct instance construction

The Fluent chain package is retired. Construct FlowApplication and FlowWorkflow
directly, add component instances and connect endpoints with LinkTo. Core owns
execution and lifetime. FluxFlow.Hosting optionally connects the application to
Generic Host. Portable definition and Engine revisions remain separate optional
capabilities; direct applications need neither a FlowGraph wrapper nor descriptors.

## Custom registration

Custom packages use the same direct factory API as builtin packages. See
[Component registration](40-component-registration.md) for synchronous/async
factories, named ports, processing profiles and ownership. Typed convenience
methods can embed that same callback; no separate contract class is required.

## Deliberate limits

- no reflection, assembly scanning, global mutable registry, or package magic;
- no delegate, registrar, handle, or node-instance serialization;
- no hidden resource ownership inference;
- no new hot-reload claim for instance-backed Fluent graphs;
- no change to canonical addresses, JSON shape, durable schemas, or delivery
  guarantees;
- no move of package/resource settings into `FluxFlowApplicationOptions`.

The simplification removes duplicate declarations and parallel execution paths;
it does not remove JSON hosting, hot reload, dynamic plugins, durable delivery,
Designer metadata, or address-based operational APIs.
