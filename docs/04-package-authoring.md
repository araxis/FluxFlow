# Package Authoring

Reusable component packages should be standalone-node-first. The package's core
job is to expose normal nodes over `FluxFlow.Nodes`. Composition adapters,
adapter-local DI helpers, and design metadata are optional layers around those
nodes.

## Default Shape

```csharp
public sealed class OrderReviewNode : FlowNode<Order, ReviewedOrder>
{
    protected override Task ProcessAsync(FlowMessage<Order> message)
    {
        var reviewed = Review(message.Value);
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

If the package wants compiled-C# and configuration composition support, expose
one complete designed contract. A single flat callback authors its exact
runtime descriptor and Designer metadata. Keep family registration familiar:
an `AddOrders(FluxFlowRegistrationBuilder)` extension registers that exact
contract for JSON hosts.

```csharp
public sealed record OrderReviewOptions
{
    public bool RequireManualApproval { get; init; } = true;
}

public static ComponentContract<OrderReviewHandle> Review { get; } =
    DesignedComponentContract.Create(
        "order.review",
        component =>
        {
            var defaults = new OrderReviewOptions();
            component.WithDisplay(
                displayName: "Order Review",
                category: "Orders",
                summary: "Reviews an order using the host policy.");
            component
                .UseFactory(CreateOrderReview)
                .HasInput("Input", static node => node.Input, displayName: "Input", isPrimary: true)
                .HasOutput("Output", static node => node.Output, displayName: "Output", isPrimary: true)
                .HasEvents("Diagnostics", static node => node.Events, displayName: "Diagnostics");
            component.AddOption<bool>(
                "RequireManualApproval",
                kind: OptionValueKind.Boolean,
                defaultValue: defaults.RequireManualApproval);
        },
        static component => new OrderReviewHandle(component));

public static FluxFlowRegistrationBuilder AddOrders(
    this FluxFlowRegistrationBuilder builder)
    => builder.AddDesignedComponent(Review);

private static OrderReviewNode CreateOrderReview(
    ComponentActivationContext context)
{
    var options = context.BindConfiguration<OrderReviewOptions>();
    var policy = context.Services.GetRequiredService<IOrderPolicy>();
    return new OrderReviewNode(policy, options);
}
```

The selected node type drives message-type inference. The one public port call
produces immutable descriptor metadata and the runtime binding; no reflection,
scanning, attributes, or property-name convention is involved. Event sources
remain `FlowEvent` streams internally and are bridged to the public
`ComponentEvent` output named by `HasEvents`. Omitting `HasEvents` means the
component has no event output, and a normal output may use the name `Events`.

For the uncommon case where a package must construct the complete runtime
instance itself, use `UseInstanceFactory(...)` and its metadata-only fluent
builder. Prefer the typed path for normal components. When only extra
completion or cleanup ownership is needed, return
`ComponentNodeActivation<TNode>` from `UseFactory(...)`; Engine then owns node
and additional cleanup exactly once, including activation-failure cleanup.

Normal component packages do not depend on Engine. Keep the default composition
path explicit and reflection-free. A code-first definition carries the exact
descriptors introduced by its contracts. JSON hosts build their catalog from
explicit contract/family registrations; packages do not own or mutate a
separate registry.

`DesignedComponentContract.Create(...)` is the normal designed-component shape.
The low-level `AddComponent(string, Action<ComponentRegistrationBuilder>)` and
`Advanced.AddDynamicComponent(string, Action<RuntimeComponentRegistrationBuilder>)`
callbacks remain for dynamic extensions. Each callback is flat, executes once,
and produces immutable catalog facts. Component families still own separate
immutable options records such as `OrderReviewOptions`; workflow-instance
values bind from the canonical `ApplicationDefinition`/JSON and do not move
into DI. Do not add a universal options type or nested descriptor, metadata,
port, option, or resource callbacks.

If the package also owns concrete resources, keep those registrations in an
adapter-local `IApplicationResourceRegistrar`. `FluxFlow.Engine` resolves those
resources from keyed DI, but the adapter still owns the concrete client/store
options and lifetime.

## Support Packages

Support packages do not need component type constants or composition registration unless
they expose actual standalone node behavior. Resource, secret, configuration,
expression, journal, design metadata, and storage-backend packages can stay as
contracts, helpers, or concrete resource factories that hosts and node adapters
consume.

## Package Rules

Each component package should own:

- component type constants when the package supports configuration composition
- node implementations
- option models and parsing helpers
- package-specific validation
- diagnostics and event names
- adapter-local DI extensions when the package owns a concrete integration
- optional DI-first component registration
- optional package-owned component definition and flat designed-component registration
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

Use it as the starting shape for new component families, then add composition
adapters only when a real host needs them.

## Versioning Guidance

Treat node type names and port names as part of the package contract when they
are exposed through composition definitions. Changing a node type or port name
can break persisted definitions, so prefer additive changes whenever possible.

Next: [Hosting And Observability](05-hosting-and-observability.md).
