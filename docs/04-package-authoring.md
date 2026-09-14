# Package Definition

Reusable component packages should be standalone-node-first. The package's core
job is to expose normal nodes over the `FluxFlow.Nodes` namespace in `FluxFlow.Core`. Composition adapters,
and adapter-local DI helpers are optional layers around those nodes.

## Default Shape

```csharp
public sealed class OrderReviewNode(IOrderPolicy policy, OrderReviewOptions options) : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        var order = message.Value!.Json.Deserialize<Order>()!;
        var reviewed = policy.Review(order, options);
        await EmitAsync(message.With(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(reviewed, new JsonSerializerOptions(JsonSerializerDefaults.Web))))), Stopping);
    }
}
```

Consumers can construct and link the node directly:

```csharp
var review = new OrderReviewNode(policy, options);
review.Output.LinkTo(sink.Input, new DataflowLinkOptions { PropagateCompletion = true });
```

## Optional Composition Registration

Register a type and its factory directly. Declare the existing native blocks on
that factory's context; no separate contract or port selector is required.

```csharp
public static IServiceCollection AddOrders(this IServiceCollection services)
    => services.AddComponent("order.review", static context =>
    {
        var options = context.BindConfiguration<OrderReviewOptions>();
        var policy = context.Services.GetRequiredService<IOrderPolicy>();
        var node = new OrderReviewNode(policy, options);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents("Diagnostics", node.Events);
        return node;
    });
```

The options and domain models are illustrative, component-owned types. Registration
does not invoke the factory. Application construction creates each node, validates
its declared ports, then compiles links against those actual instance ports. Failed
port or link validation disposes newly created instances before processing starts.
Use HasInput("Open", node.Open, signal: true) for a terminal control. Named
inputs/outputs support link cardinality, aliases and multiple event sources.
Input/control names share one namespace; normal/event outputs share another.

Re-registering the same factory delegate for a type is idempotent; conflicting
factories are rejected. Component packages do not require Engine.

Async initialization uses the same AddComponent overload with an async callback
returning the node. Declare ports on the same context after awaiting initialization.
When the component owns an additional resource, call
context.SetLifetime(disposeAsync: resource.DisposeAsync) before returning the node.
An optional completion task preserves the existing explicit lifetime behavior.
The node is always disposed first; additional cleanup still runs if that fails.
This ownership is transferred only when the factory returns its node: if construction
throws before returning, the factory must release resources it already acquired.
Do not register cleanup for resources borrowed from the host.

All 45 built-in registrations (including Gate) use context callbacks. Their 44 typed
AddX convenience methods share those same factories through ApplicationSetup.ComponentFactories.
All builtin and custom component contract APIs are removed. Custom components
use the same direct context registration as builtin packages.

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
