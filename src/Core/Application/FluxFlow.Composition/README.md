# FluxFlow.Composition

Direct Composition builds support resources inside workflows, using existing
local-first/full-address rules and application-owned disposal. MQTT dependencies
and processing profiles support these locations. Engine revision hosting remains
deferred. See [workflow connection examples and registrar guidance](../../../../docs/plans/2026-09-13-workflow-connections.md).

Code-first definition, explicit component registration,
validation, link compilation, and hosted runtime ownership. Portable documents
and their parser are owned by FluxFlow.Json. The package is
Engine-independent.

Shared addresses are owned by Core. Import `FluxFlow.Core.Addressing` for ApplicationAddress and ApplicationAddressKind; address strings and resolution semantics are unchanged. This package consumes that existing address type and no longer declares a separate assembly-owned copy.

## Plain port metadata

Physical declarations have one canonical model: sealed `ComponentInputPort` and
`ComponentOutputPort` hold native `ITargetBlock<FlowMessage>` and
`ISourceBlock<FlowMessage>`. Construct them directly or use `ComponentPorts.Input`,
`SignalInput`, and `Output`. They add no queues, conversions or lifetime protocol.
The component and host retain their existing lifetime responsibilities.

Factories register native blocks directly through context.HasInput/HasOutput/HasEvents
and return their node. All built-in families use this path, sharing callbacks with
typed C# AddX methods. ApplicationSetup.ComponentFactories snapshots embedded
callbacks; application construction validates actual ports before starting processing.
No separate built-in component contract or binding selector is required. Async
callbacks can transfer additional ownership through context.SetLifetime. HTTP's
configuration binding explicitly declares its supported parallel-processing capability.

Runtime port declarations remain canonical and retain cardinality, signal and event semantics.

Control bindings now take `ITargetBlock<FlowMessage>` directly, including
context declarations with `signal: true`, `ComponentPorts.SignalInput`, and keyed control registration.
Resolve keyed controls as `ITargetBlock<FlowMessage>`; the custom signal-target
interface is removed. Control kind describes terminal lifetime, not a second
transport. Host completion or revision detachment does not complete the
component-owned control block. Typed caller convenience converts values to
canonical messages before admission; arbitrary CLR object identity is not a
control transport guarantee.

The development major removes `IComponentPortTypeVisitor`, `SupportsTypeVisit`,
and `Accept`. Consumers inspecting metadata should read `Name`, `Kind`,
`MessageType` and `LinkCardinality` directly. Business
ports in hosted workflows still require canonical `FlowMessage` blocks; this
change does not reintroduce typed runtime routing.

## Input Validation

Components own deserialization and domain validation. Read FlowValue.Json using
local System.Text.Json metadata, or reuse a compatible typed FlowValue directly.
No framework materializer interface or result wrapper is required. Runtime
processing failures retain their FlowError and original diagnostic exception.


## Application Shape

```json
{
  "Resources": {},
  "Workflows": {
    "Orders": {
      "Receive": { "Type": "source" },
      "Handle": {
        "Type": "handler",
        "Input": "Receive.Output"
      }
    }
  }
}
```

Resources, workflows, and components are named by object keys. Components are
flat; there are no maintained Composition, Nodes, or root Links wrappers.
Addresses are ordinal and case-sensitive. Links support fan-in, fan-out,
conditions, cross-workflow addresses, and explicit bounded signal feedback.
Ordinary data-processing cycles are rejected.

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
