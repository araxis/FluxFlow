# Component registration

Composition is optional. Direct Core applications construct nodes and connect
their ports normally. Configuration-driven applications register a type name and
a factory that returns a node and declares its actual ports on the context.

## Register a factory

```csharp
static ValueTask<IFlowNode> CreateUppercase(ComponentActivationContext context)
{
    var node = new UppercaseNode();
    context.HasInput(node.Input);
    context.HasOutput(node.Output);
    context.HasEvents(node.Events);
    return ValueTask.FromResult<IFlowNode>(node);
}

services.AddComponent("sample.uppercase", CreateUppercase);
```

A synchronous factory returning `IFlowNode` is also supported. Registration does
not construct a node. Each activation receives its own context, constructs a fresh
node, and declares only ports that actually exist. Use `HasInput("Start", node.Input)`
to give a port a different address name; use `signal: true` for a terminal control
input. `HasEvents("Diagnostics", node.Events)` declares an addressable event output.
No implicit input, output or event port is added. Port declarations create no new
business-data queues and accept only canonical `FlowMessage` blocks.

Factories may read component-owned options through `BindConfiguration<T>()` and
resolve dependencies from `context.Services`. Payload conversion stays inside the
component. A factory with supported parallel processing passes its capability to
`BindConfiguration<T>(CompositionProcessingCapabilities.ParallelRelaxedOrder)`.
Unsupported processing profiles fail construction.

## Share the factory with C# definitions

```csharp
var builder = new ApplicationDefinitionBuilder();
var workflow = builder.AddWorkflow("main");
var upper = workflow.AddComponent("upper", "sample.uppercase", CreateUppercase,
    static definition => { },
    static component => new InputOutputComponentHandle<FlowValue, FlowValue>(
        component, "Input", "Output", "Events"));
upper.Output.DiscardUnmatched();
upper.Events.DiscardUnmatched();
var setup = builder.Build();
await using var application = await setup.BuildApplicationAsync(serviceProvider);
```

Package-specific `AddX` extension methods can package this small definition call
and return meaningful typed handles. They share the same factory used by DI.
`ApplicationSetup.ComponentFactories` snapshots these callbacks. There is no
second registration step for embedded callbacks. Duplicate entries with the same
callback are accepted; different callbacks claiming one type are rejected.

JSON contains configuration and links, never executable callbacks. A host loading
JSON registers the component families it uses. Build links against the created
instances: the real ports are the authority for addresses and link validation.
`ApplicationLinkCompiler` takes instances indexed by `workflow.component`.

## Ownership and failure

Async factories return `ValueTask<IFlowNode>`. If a factory acquires an owned
resource in addition to its node, attach that cleanup with
`context.SetLifetime(disposeAsync: lease.DisposeAsync)`. A separate completion task
can be supplied when required. Shared host resources remain host-owned.

A factory must clean up resources if it throws before returning its node. Once a
node is returned, failed port validation or graph construction releases it and its
attached ownership. Cleanup failures remain visible alongside the original error.
Do not return a node already owned by another graph.

Engine combines embedded and DI factories for each candidate revision. Changing
a workflow's factory callback requires a new revision; unchanged callbacks can
reuse the existing generation. Registration does not infer resource ownership.

## Removed APIs

`ComponentContract` and its generic variants, `ComponentNodeActivation`,
`ComponentPortBinding`, `CreateBound`, `CreateStandard`, `CreateNative`,
`CreateComponentMap`, `ApplicationSetup.ComponentContracts` and the empty
`AddFluxFlowComponents()` bootstrap are removed. Use direct `AddComponent(type,
factory)`, context port declarations and `SetLifetime` instead. Resource contracts
are a separate supported resource feature and are unchanged.
