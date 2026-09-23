# Component registration

Components expose native Dataflow blocks and own their processing and cleanup.
Registration chooses names and dependencies; it does not construct nodes.

While reading each component section, setup also parses its registered input/output
properties and registers a LinkFactory for each unique declaration. Arrays, either
declaration direction, named ports, relative addresses, conditions, parameters and
fallback/delivery settings retain the flat grammar. Syntax errors and conflicting
duplicate policies reject registration transactionally. Build resolves the captured
addresses and compiles conditions once per provider; it does not scan the document
again to discover links. Engine selection is local to each condition:
`{"Port":"other.sink.Input","Condition":"value > 0","Engine":"csharp"}`.
There is no application/workflow ExpressionEngine setting. Mapper and Router retain
their own Engine configuration. Custom engine names are native DI keys, not enum values.

There is no whole-document link scan or intermediate definition tree. Each section
registers LinkFactory delegates directly. Native C# predicates use ports.Link in
the same delegates.

Native resource helpers live alongside component registration in
`FluxFlow.Core.Application`: `ResourceRegistrationExtensions` provides
`AddFluxFlowResource` and `AddExternalFluxFlowResource`, and `FlowResourceExtensions`
provides `GetRequiredFlowResource`. They use keyed DI without reading configuration.

Ordinary resource registration follows the same timing. Configuration's keyed
ResourceRegistration delegate receives IServiceCollection, the resource address and its
IConfigurationSection during AddFluxFlowApplicationAsync. Register a native resource factory there.
The former Core callback receiving a host provider at Build is removed. For code-only
registration, AddApplicationResources executes its callback immediately and freezes the
descriptors. Native DI owns resource instances and their dependent components.
FlowApplication now coordinates resource construction, component construction and linking
through private methods; StartAsync remains separate. ResourceRegistration is used by configuration setup.
It receives IServiceCollection, ApplicationAddress and IConfigurationSection;
it has no provider, revision ID or revision context. Configuration invokes per-type callbacks before constructing the host provider.

Ordinary setup traverses IConfiguration directly, without an intermediate definition
tree. The former explicit document models, serializers and diagnostic compiler are removed.
IConfiguration registrations currently cover Scheduler, Gate, Source, Mapper, Router and Batch through
AddScheduler/AddGate/AddSource/AddMapper/AddRouter/AddBatch in FluxFlow.Configuration. Each uses a plain configuration
DTO and an independent registration method; no central component switch or catalog.
See the Configuration package README for a Scheduler → Source → Gate example and
the ValuesJson format that preserves heterogeneous sample types.

Mapper resolves a keyed IFlowExpressionEngine during Build and passes its normal delegate
to MapperOptions. Optional AddCSharpExpressionEngine/AddJsonataExpressionEngine registrations live
in separate engine packages. Configuration reading does not instantiate a component,
read an expression file or compile its expression. Build compiles once per component,
before Start; partial failure uses normal component/resource cleanup. No engine/context
is passed to Mapper. The callback receives FlowMessage so setup can expose separate
value/headers/string-ID parameters while Mapper retains message lineage.
The same engine registration now supports conditional links and bound parameters;
there is no separate Configuration.ExpressionCompiler registration.

For a known address, register the factory and native port selectors with
`AddComponentFactory`, `AddInputPort`, `AddOutputPort` and `AddEventPort` in
`FluxFlow.Core.Application`. Call `provider.BuildApplicationAsync()` to construct
an unstarted application. Then use its StartAsync, StopAsync and DisposeAsync.

Link delegates resolve native blocks from DI, with no FlowInputEndpoint wrapper:

```csharp
services.AddSingleton<LinkFactory>(ports =>
    ports.Link("main.source.Output", "other.sink.Input"));

await using var provider = services.BuildServiceProvider();
await using var application = await provider.BuildApplicationAsync();
await application.StartAsync();
// Configured sources now generate messages; connected blocks process them.
```

Application/workflow GetInput and application.SendAsync are removed. During Build,
`ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>(address)` resolves that same
block. Aliases may name the same input without creating another block or transferring
disposal to DI. FlowLink exposes InputAddress/OutputAddress and native Input/Output
blocks. Application/workflow GetOutput and FlowOutputEndpoint are removed.
Resolve a native source with `ports.GetRequiredKeyedService<ISourceBlock<FlowMessage>>(address)`.
Direct output
LinkTo/OtherwiseTo are removed; use ports.Link/LinkFallback in LinkFactory.
This is a breaking development-preview API change; rebuild consumers.

Section-based setup selects the existing keyed ComponentRegistration by the
document's Type. Its signature is
`ComponentRegistration(IServiceCollection, IConfigurationSection)` in
FluxFlow.Configuration. Read the separate configuration DTO, then register the
native factory and port selectors. The old definition-based registration overload
in Core was unused and is removed; there is no replacement registration layer.

The synchronous `ComponentFactory(IServiceProvider)` delegate remains unchanged.
The asynchronous factory overload is removed. Factories do not receive Application,
Workflow, a configuration context or a cancellation token. Build checks cancellation
between factory calls and stops constructed nodes on failure. The host then disposes its provider.
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
