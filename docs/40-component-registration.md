# Component registration

Components expose native Dataflow blocks and own their processing and cleanup.
Registration chooses names and dependencies; it does not construct nodes.

Each component registrar binds its DTO once, then explicitly registers links from
that DTO using AddInputLinks, AddOutputLinks and, when supported, AddErrorLinks.
ApplicationConfigurationExtensions only traverses sections and invokes registrars; it does not
discover port properties or infer their direction from DI. Arrays, either
declaration direction, named ports, relative addresses and conditions retain the flat
grammar. Syntax errors reject registration transactionally. Equal links declared
from both ends share one keyed registration. Build resolves the captured
addresses and compiles conditions once per provider; it does not scan the document
again to discover links. Engine selection is local to each condition:
`{"Address":"other.sink.Input","Condition":"value > 0","Engine":"csharp"}`.
There is no application/workflow ExpressionEngine setting. Mapper and Router retain
their own Engine configuration. Custom engine names are native DI keys, not enum values.

There is no whole-document link scan or intermediate definition tree. Each section
registers LinkFactory delegates directly. Native C# predicates use ports.Link in
the same delegates.

A single unconditional connection needs only
`services.AddLink(sourcePortAddress, targetPortAddress)` from
`FluxFlow.Configuration.Application.Links`.
Both arguments are PortAddress values, with source first. No link collection is needed.
It shares deferred registration and deduplication with the plural configuration helpers.
Registering one link does not impose a global one-connection limit on either port.

Native resource helpers live alongside component registration in
`FluxFlow.Application`: `ResourceRegistrationExtensions` provides
`AddFluxFlowResource` and `AddExternalFluxFlowResource`, and `FlowResourceExtensions`
provides `GetRequiredFlowResource`. They use keyed DI without reading configuration.

Ordinary resource registration follows the same timing. Configuration's keyed
ResourceRegistration delegate receives IServiceCollection, the resource address and its
IConfigurationSection during AddFluxFlowApplication. Register a native resource factory there.
The former Core callback receiving a host provider at Build is removed. For code-only
registration, AddApplicationResources executes its callback immediately and freezes the
descriptors. Native DI owns resource instances and their dependent components.
FlowApplication now coordinates resource construction, component construction and linking
through private methods; StartAsync remains separate. ResourceRegistration is used by configuration setup.
It receives IServiceCollection, ApplicationAddress and IConfigurationSection;
it has no provider, revision ID or revision context. Configuration invokes per-type callbacks before constructing the host provider.

Ordinary setup traverses IConfiguration directly, without an intermediate definition
tree. The former explicit document models, serializers and diagnostic compiler are removed.
For example, Scheduler, Gate, Source, Mapper, Router and Batch use
AddScheduler/AddGate/AddSource/AddMapper/AddRouter/AddBatch in their respective
`FluxFlow.Configuration.Components.*` namespaces. Each uses a plain configuration
DTO and an independent registration method; no central component switch or catalog.
See the Configuration package README for a Scheduler → Source → Gate example and
the Values format that preserves heterogeneous sample types.

Mapper resolves a keyed IFlowExpressionEngine during Build and passes its normal delegate
to MapperOptions. Optional AddCSharpExpressionEngine/AddJsonataExpressionEngine registrations live
in separate engine packages. Configuration reading does not instantiate a component,
read an expression file or compile its expression. Build compiles once per component,
before Start; partial failure uses normal component/resource cleanup. No engine/context
is passed to Mapper. The callback receives FlowMessage so setup can expose separate
value/headers/string-ID parameters while Mapper retains message lineage.
The same engine registration supports conditional links with the standard message context;
there is no separate Configuration.ExpressionCompiler registration.

For a known address, register the factory and native port selectors with
`AddComponentFactory`, `AddInputPort`, `AddOutputPort` and `AddEventPort` in
`FluxFlow.Application`. Call `provider.BuildApplicationAsync()` to construct
an unstarted application. Then use its StartAsync, StopAsync and DisposeAsync.

AddFlowWorkflow registers a keyed FlowWorkflow singleton plus its enumeration alias.
GetServices<FlowWorkflow>() discovers workflows; GetRequiredKeyedService<FlowWorkflow>(name)
resolves one. ComponentAddress values registered under each workflow name let Build
enumerate component addresses without duplicating disposable node registrations.
Components retain exactly one scoped DI ownership registration per workflow run. Application.Workflows
is a read-only collection, and workflow instances contain no component lookup dictionary.

For standard port names, inherit the data-only ComponentConfiguration record and call
services.AddLinks(address, config) in the registrar. Scheduler instead inherits
OutputComponentConfiguration and exposes no Input. Both use OneOrManyPortReferences and the same
link identity, condition compilation and Error filtering as the individual helpers.
Special ports remain explicit calls; no scan infers links from native port declarations.

Link delegates resolve native blocks from DI, with no FlowInputEndpoint wrapper:

```csharp
services.AddSingleton<LinkFactory>(ports =>
    ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("other.sink.Input")));

await using var provider = services.BuildServiceProvider();
await using var application = await provider.BuildApplicationAsync();
await application.StartAsync();
// Configured sources now generate messages; connected blocks process them.
```

Application/workflow GetInput and application.SendAsync are removed. During Build,
`ports.GetWorkflowServices(workflowName).GetRequiredKeyedService<ITargetBlock<FlowMessage>>(address)` resolves that same
block. Aliases may name the same input without creating another block or transferring
disposal to DI. FlowLink exposes InputAddress/OutputAddress and native Input/Output
blocks. Application/workflow GetOutput and FlowOutputEndpoint are removed.
Resolve a native source with `ports.GetWorkflowServices(workflowName).GetRequiredKeyedService<ISourceBlock<FlowMessage>>(address)`.
Direct output
LinkTo/OtherwiseTo are removed; use ports.Link in LinkFactory. Conditions filter each
link independently; no match consumes the message without faulting execution.
This is a breaking development-preview API change; rebuild consumers.

Section-based setup selects the existing keyed ComponentRegistration by the
document's Type. Its signature is
`ComponentRegistration(IServiceCollection, IConfigurationSection)` in
`FluxFlow.Configuration.Components`. Read the separate configuration DTO, then register the
native factory, port selectors and links. For example, inside a registrar:

```csharp
var address = section.GetComponentAddress();
var config = section.ReadRequiredConfiguration<MyConfiguration>();
services.AddComponentFactory(address, dependencies => new MyComponent());
services.AddInputPort<MyComponent>(address, "Input", node => node.Input);
services.AddOutputPort<MyComponent>(address, "Output", node => node.Output);
services.AddEventPort<MyComponent>(address, "Events", node => node.Events);
services.AddLinks(address, config);
```

Here `MyConfiguration` inherits `ComponentConfiguration`, providing Input, Output,
Events and Error. `AddLinks` registers these standard names; use the individual
AddInputLinks/AddOutputLinks/AddErrorLinks helpers when a component has different ports.
The four DTO properties are OneOrManyPortReferences, defaulting to Empty. The helpers belong
to FluxFlow.Configuration.Application.Links. GetComponentAddress returns ComponentAddress, and
Port(name) produces a validated PortAddress. Each bound PortReference.Address
is already a full PortAddress: relative references resolve during ReadConfiguration
using the component section's workflow. Invalid address syntax fails that read with
the property location, before link registration. Strings cannot be passed to link
helpers. Application registration and links retain these typed addresses; Value is used only for native DI keys.
Port existence and input/output direction still require registered blocks at Build.
They register existing LinkFactory delegates. Error
filters IsError on Output; it adds no runtime port or component setting. Output
keeps delivering both normal and error messages, so an error can reach both selections.
Custom registrars must call these helpers explicitly; registering a native port alone
no longer discovers configuration links. A registrar can map a differently named DTO
property to its port. Router reads only its known dynamic route properties locally.

The old definition-based registration overload
in Core was unused and is removed; there is no replacement registration layer.

The synchronous `ComponentFactory(IServiceProvider)` delegate remains unchanged.
It is supplied to AddComponentFactory and captured by the keyed scoped IFlowNode
factory. The redundant separately keyed ComponentFactory service is removed; resolve
IFlowNode to obtain the DI-owned component rather than invoking a factory yourself.
The asynchronous factory overload is removed. Factories do not receive Application,
Workflow, a configuration context or a cancellation token. Build checks cancellation
between factory calls and stops constructed nodes on failure, disposing their workflow scopes.
Factories run again after workflow restart and must construct fresh nodes. Reacquire scoped
services and ports after restart; do not resolve them from the root or unrelated scopes.
Factories must release anything they allocated before throwing without returning a node.

Component registration binds its own typed configuration and constructs options explicitly.
The generic definition.BindConfiguration helper and its JSON property injection are removed.
Resolve a resource using `services.GetRequiredFlowResource<T>(reference,
workflowName)` from FlowResourceExtensions. The definition's GetRequiredResource/GetResource
extensions and definition-based AddProcessingProfile are removed; setup reads its section
and registers/resolves native services directly.
Dependencies passed to a node remain explicit; components never receive a container.

Each adapter maps its supported settings directly to component options and native
Dataflow execution options. Components validate their own processing constraints,
such as ordered serial execution for Batch. No shared processing-profile, capability
or profile-mapper layer is required. Existing component defaults remain in effect
for settings an adapter does not expose.

Port selectors receive the actual node. A different address name is simply another
name argument. Signal behavior is an explicit optional setting;
terminal control lifetime is still determined by native block identity in TerminalInputs.
Named events get address headers and a routable output. Observation uses bounded,
best-effort native broadcast; it neither owns a second component nor changes its lifetime.
No input/output/event port is inferred. Reserved output-policy names are rejected during
port registration, before document link compilation.

Hot reload and revision resource contexts are removed. Ordinary components and
resources use the one host-owned native DI provider.
