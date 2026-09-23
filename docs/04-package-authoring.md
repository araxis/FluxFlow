# Package authoring

Reusable packages should be standalone-node-first. Component implementations depend on
FluxFlow.Core and expose native blocks from `FluxFlow.Core.Nodes` or Dataflow directly.
Keep options and processing separate from configuration reading and application naming.

## Registration outside components

An adapter can expose an ordinary IServiceCollection extension. The following illustrates
a known-address registration; OrderReviewNode, IOrderPolicy and its options are domain types.

```csharp
public static IServiceCollection AddOrders(this IServiceCollection services,
    string address, OrderReviewOptions options)
    => services.AddComponentFactory(address, provider =>
            new OrderReviewNode(provider.GetRequiredService<IOrderPolicy>(), options))
        .AddInputPort<OrderReviewNode>(address, "Input", node => node.Input)
        .AddOutputPort<OrderReviewNode>(address, "Output", node => node.Output)
        .AddEventPort<OrderReviewNode>(address, "Diagnostics", node => node.Events);
```

The callback is synchronous and lazy. Construction runs once per address in the host
provider. The native ports are selected from that same instance. Component code receives
explicit dependencies, never the provider, application or workflow. Constructor options
can contain ordinary mapping/predicate delegates without knowing their expression engine.

## Configuration adapters

Keep configuration DTOs separate from runtime options. A registration method reads the
IConfigurationSection, validates it, captures settings and registers the factory above.
Option construction belongs in that method/factory, not in the DTO. The current adapters
live in FluxFlow.Configuration; Scheduler, Gate, Source and Mapper packages remain free
of configuration dependencies. Register component types using keyed registration delegates,
without a central switch or catalog. See [Component registration](40-component-registration.md).

## Ownership

Native DI owns components and their DI dependencies. Input/output selectors must return
non-disposable Dataflow blocks; components own block cleanup. A node instance cannot be
registered under multiple addresses/providers. Failed Build stops constructed nodes;
the host still disposes its provider. Standard DI stops at the first disposal exception.
Async activation belongs in startup, not an asynchronous constructor/factory.

## Package contents

- Component behavior and its options.
- Component-local value models and conversions.
- Focused behavioral tests, including failure, cancellation and bounded delivery.
- Separate configuration adapters where required.
- A small working example using native DI and LinkFactory.

Avoid discovery by assembly scanning, hidden service lookup, UI metadata and speculative
factory/context hierarchies. Names and ports exposed to configuration are part of its
contract; changing them requires an intentional migration.

Each component module has its own project under src/Components and the Visual Studio
Components solution folder. See current examples in FluxFlow.Components.Scheduler and
FluxFlow.Configuration rather than retired component-package templates.
