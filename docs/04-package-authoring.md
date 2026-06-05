# Package Authoring

Reusable component families should be shipped outside `FluxFlow.Engine`.
The engine provides small registration helpers so package authors can expose
their nodes as an explicit group.

## Module Shape

```csharp
public sealed class OrderModule : IFlowNodeModule
{
    public OrderModule(IOrderStore store)
    {
        Registrations =
        [
            new FlowNodeRegistration(OrderNodeTypes.Source, OrderSourceNode.Create),
            new FlowNodeRegistration(OrderNodeTypes.Review, OrderReviewNode.Create),
            new FlowNodeRegistration(
                OrderNodeTypes.Sink,
                context => OrderSinkNode.Create(context, store))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
```

Register a module directly:

```csharp
var registry = new RuntimeNodeFactoryRegistry()
    .Register(new OrderModule(store));
```

Or expose a package-owned extension:

```csharp
public static RuntimeNodeFactoryRegistry RegisterOrderComponents(
    this RuntimeNodeFactoryRegistry registry,
    IOrderStore store)
{
    return registry.Register(new OrderModule(store));
}
```

## Registration Safety

`RegisterRange` and module registration validate duplicate node types before
mutating the registry. If a group contains a duplicate, the registry is left
unchanged.

The existing registry duplicate check still applies when registering one item at
a time.

## Package Rules

Each component package should own:

- node type constants
- node implementations
- option models and parsing helpers
- package-specific validation
- diagnostics and event names
- registration module
- package-owned design metadata provider when designer-friendly host
  composition is useful
- tests
- a small runnable sample when useful

Avoid:

- assembly scanning
- reflection-based discovery
- global mutable state
- hidden dependency lookups
- app workspace schemas
- renderer-specific UI metadata

Dependencies should be passed through constructors, delegates, or package-owned
options.

## Design Metadata

Reusable packages can expose an `IComponentDesignMetadataProvider` from
`FluxFlow.Components.Designer` when hosts need package-owned palette entries,
option editor hints, port labels, validation-facing option shape, or generated
documentation.

Keep this metadata neutral and tied to the package's public node type constants.
Hosts compose package providers into a `ComponentDesignMetadataCatalog`, then add
app-specific rendering, localization, resource pickers, and behavior overrides
outside the package descriptor.

## Copyable Template

The repository includes a small buildable template under
`samples/FluxFlow.ComponentPackageTemplate`. It contains one transform node and
the expected package pieces:

- contracts
- options and option parsing
- diagnostics and error codes
- node type and port constants
- node implementation
- module and registry extension
- design metadata provider when useful
- focused tests

Use it as the starting shape for new component families, then replace the
sample contracts and node with the real package contract.

## Versioning Guidance

Treat node type names and port names as part of the package contract. Changing a
node type or port name can break persisted definitions, so prefer additive
changes whenever possible.

Next: [Hosting And Observability](05-hosting-and-observability.md).
