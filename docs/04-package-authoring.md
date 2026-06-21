# Package Authoring

Reusable component packages should be standalone-node-first. The package's core
job is to expose normal nodes over `FluxFlow.Nodes`; composition adapters,
engine modules, DI helpers, and design metadata are optional layers.

## Default Shape

```csharp
public sealed class OrderReviewNode : FlowNode<Order, ReviewedOrder>
{
    protected override Task ProcessAsync(FlowMessage<Order> message)
    {
        var reviewed = Review(message.Payload);
        Emit(message.With(reviewed));
        return Task.CompletedTask;
    }
}
```

Consumers can construct and link the node directly:

```csharp
var review = new OrderReviewNode();
review.Output.LinkTo(sink.Input, new DataflowLinkOptions { PropagateCompletion = true });
```

## Optional Composition Registration

If the package wants fluent/config composition support, expose a small extension
that registers explicit factories with `CompositionNodeRegistry`:

```csharp
public static CompositionNodeRegistry RegisterOrderNodes(
    this CompositionNodeRegistry registry,
    IOrderPolicy policy)
{
    return registry.Register(
        "order.review",
        _ =>
        {
            var node = new OrderReviewNode(policy);
            return ValueTask.FromResult(ComposedNode.Create(
                node,
                inputs: [CompositionPorts.Input<Order>("Input", node.Input)],
                outputs: [CompositionPorts.Output<ReviewedOrder>("Output", node.Output)]));
        },
        inputs: [CompositionPorts.Metadata<Order>("Input")],
        outputs: [CompositionPorts.Metadata<ReviewedOrder>("Output")]);
}
```

Use engine `IFlowNodeModule` only for packages that intentionally support the
optional engine runtime. It is not required for normal component packages.

If the package also owns concrete resources, keep those registrations in an
adapter-local DI extension. `FluxFlow.Composition.Hosting` can resolve those
resources from keyed DI, but the adapter still owns the concrete client/store
options and lifetime.

## Package Rules

Each component package should own:

- node type constants when the package supports composition or engine definitions
- node implementations
- option models and parsing helpers
- package-specific validation
- diagnostics and event names
- adapter-local DI extensions when the package owns a concrete integration
- optional composition registration
- optional engine module
- optional design metadata provider
- tests
- a small runnable sample when useful

Avoid:

- assembly scanning
- reflection-based discovery
- global mutable state
- hidden dependency lookups
- app workspace schemas
- renderer-specific UI metadata
- forcing engine dependencies into standalone node packages

Dependencies should be passed through constructors, delegates, options, or
adapter-owned DI.

## Copyable Template

The repository includes a small buildable standalone-node template under
`samples/FluxFlow.ComponentPackageTemplate`. It contains one transform node and
the expected package pieces:

- contracts
- options
- diagnostics and error codes
- node implementation
- focused tests

Use it as the starting shape for new component families, then add composition or
engine adapters only when a real host needs them.

## Versioning Guidance

Treat node type names and port names as part of the package contract when they
are exposed through composition or engine definitions. Changing a node type or
port name can break persisted definitions, so prefer additive changes whenever
possible.

Next: [Hosting And Observability](05-hosting-and-observability.md).
