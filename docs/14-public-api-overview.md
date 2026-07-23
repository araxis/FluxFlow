# Public API Overview

FluxFlow's default public surface is standalone-node-first:

- `FluxFlow.Data` for transport-neutral values, content, and result contracts.
- `FluxFlow.Nodes` for node authoring.
- `FluxFlow.Composition` for canonical definitions, link compilation, and
  fluent/config composition of standalone nodes.
- `FluxFlow.Engine` for the optional advanced engine runtime.

The release tests maintain a lightweight public API baseline for package source
declarations. Treat baseline changes as a prompt to review package versioning,
changelog entries, and documentation before accepting the new public surface.

## Data Foundation

Namespace:

```text
FluxFlow.Data
```

Main types:

- `FlowValue` and `FlowValueKind`
- `FlowValueCanonicalJson`
- `FlowContent`
- `IFlowContentCodec`
- `FlowContentCodecCatalog`
- `FlowContentCodecRegistration`
- `IFlowResult`
- `FlowResult<T>`
- `FlowError`

Use `FlowValue` when a component needs transport-neutral dynamic content and
`FlowContent` when exact ingress bytes and content metadata must remain
available alongside lazy structured decoding. These contracts do not depend on
Dataflow, composition, hosting, or a component family.

## Node Kit

Namespace:

```text
FluxFlow.Nodes
```

Main types:

- `FlowMessage<T>`
- `CorrelationId`
- `TraceId`
- `MessageId`
- `FlowNode<TInput,TOutput>`
- `FlowNodeOptions`
- `FlowSource<TOutput>`
- `FlowSourceOptions`
- `IFlowNode`
- `IFlowSource`
- `IFlowSignalTarget`
- `FlowError`
- `FlowEvent`
- `FlowEventLevel`

Use these types to author standalone nodes directly. `FlowNodeOptions`
configures bounded transform intake and validates non-positive capacities and
parallelism values when assigned. `FlowSourceOptions` lets source nodes opt into
bounded broadcast output and awaitable output-block acceptance while sources
that do not pass options keep the original unbounded broadcast behavior. It
allows `UnboundedOutputCapacity` and validates other output capacities when
assigned. `FlowMessage<T>` separates business correlation, graph trace,
per-hop identity, and causation. Its headers are immutable ordinal `FlowValue`
entries. `FlowEvent` attributes continue to copy assigned dictionaries with
ordinal key comparison.

`IFlowSignalTarget.SendAsync<T>` is the standalone, payload-independent signal
input contract. It reports acceptance as a Boolean and retains normal
`FlowMessage<T>` identity without adding routing or correlation behavior.

## Composition

Namespace:

```text
FluxFlow.Composition
```

Main types:

- `FluxFlow.Composition.Model.ApplicationDefinition`
- `FluxFlow.Composition.Model.WorkflowDefinition`
- `FluxFlow.Composition.Model.ComponentDefinition`
- `FluxFlow.Composition.Model.ResourceDefinition`
- `FluxFlow.Composition.Model.ResourceGroupDefinition`
- `FluxFlow.Composition.Model.ResourceInstanceDefinition`
- `FluxFlow.Composition.Model.ApplicationDefinitionJson`
- `FluxFlow.Composition.Model.ApplicationDefinitionNormalizer`
- `FluxFlow.Composition.Model.ApplicationDefinitionNormalizationResult`
- `FluxFlow.Composition.Model.ApplicationDefinitionNormalizationDiagnostic`
- `FluxFlow.Composition.Addressing.ApplicationAddress`
- `FluxFlow.Composition.Addressing.ApplicationAddressKind`
- `FluxFlow.Composition.Links.ApplicationLinkCompiler`
- `FluxFlow.Composition.Links.ApplicationLinkCompilationResult`
- `FluxFlow.Composition.Links.CompiledApplicationLink`
- `FluxFlow.Composition.Links.ApplicationLinkDiagnostic`
- `FluxFlow.Composition.Links.ApplicationSystemOutputMetadata`
- `FluxFlow.Composition.Revisions.ApplicationRevisionPlanner`
- `FluxFlow.Composition.Revisions.ApplicationRevisionPlan`
- `FluxFlow.Composition.Revisions.ApplicationRevisionEvent`
- `FluxFlow.Composition.Revisions.IApplicationRevisionEventSink`
- `ApplicationDefinitionConfigurationLoader`
- `CompositionComponentTypeDescriptor`
- `CompositionComponentEvent`
- `CompositionComponentEvents`
- `CompositionProcessingProfile`
- `CompositionProcessingMode`
- `CompositionProcessingOrder`
- `CompositionProcessingBuffer`
- `CompositionProcessingCapabilities`
- `CompositionProcessingSettings`
- `ICompositionProcessingProfileMapper`
- `DefaultCompositionProcessingProfileMapper`
- `CompositionProcessingResourceTypes`

The canonical document is immutable and has exactly two case-sensitive
root objects: `Resources` and `Workflows`. Resource groups form nested address
namespaces and resource leaves require `Type`; workflows directly contain flat
component objects that also require `Type`. `ApplicationAddress` represents
resource paths, absolute workflow components and ports, local port resolution,
and the reserved system event/diagnostic outputs with ordinal equality.
`Workflow.Component` is the canonical component key;
`ResolvePort("Component.Port", workflow)` remains the local-port resolver.
`ApplicationDefinitionNormalizer` resolves registered component aliases and
known resource aliases with structured diagnostics before validation,
revision comparison, Designer projection, or activation.

`ApplicationLinkCompiler` reads links from registered input or output port
properties, normalizes absolute source/target addresses, compiles expression
conditions once, and reports static diagnostics for invalid endpoints, exact
type mismatches, duplicate or exclusive claims, and cycles. Successful links
preserve their declaration side for Designer persistence. Engine-owned system
streams contribute type metadata through `ApplicationSystemOutputMetadata`.
The compiler does not activate or route links.

`ApplicationRevisionPlanner` compares complete canonical definitions, computes
resource/workflow changes and transitive resource dependents, and rejects
missing resource references or dependency cycles. Shared revision events are
normal transport records; hosts provide the event sink and activation policy.

Every `CompositionNodeRegistration` reserves an addressable `Events` output
carrying traced `CompositionComponentEvent` values. Normal results and expected
failures remain on `Output`, normally as `FlowResult<T>`; unrecoverable faults
remain on `Completion`. `CompositionProcessingProfile` provides optional
semantic mode/order/buffer policy, and the DI mapper translates it to technical
settings only for registrations that declare matching capabilities.

The following obsolete types remain the executable composition compatibility
surface until the next major cleanup:

- `CompositionDefinition`
- `WorkflowDefinition`
- `NodeDefinition`
- `LinkDefinition`
- `NodeReference`
- `PortReference`
- `CompositionDefinitionBuilder`
- `CompositionConfigurationLoader`
- `CompositionNodeRegistry`
- `CompositionNodeRegistration`
- `CompositionNodeFactoryContext`
- `ComposedNode`
- `CompositionPorts`
- `CompositionPortMetadata`
- `CompositionValidator`
- `CompositionRuntimeBuilder`
- `CompositionRuntime`
- `CompositionBuildResult`
- `ICompositionDefinitionSource`
- `ICompositionReloadPlanner`

Use the compatibility types only for existing direct standalone-node
composition from fluent C# or legacy `IConfiguration` JSON. Definition
DTO collection properties copy assigned dictionaries and lists with ordinal key
comparison so caller-owned collections cannot mutate a built definition.
Workflow, node, configuration, and resource dictionary keys are trimmed when
assigned or built fluently; duplicate keys after trimming are rejected.
Node and port references trim assigned segments and reject empty dotted segments
when parsed from fluent or configuration link strings.
Node definition types, node registration types, and composition port metadata
names are trimmed at the public boundary so configuration and adapter
registrations agree on stable identifiers.
`ComposedNode` disposal attempts node disposal and descriptor cleanup hooks
independently, and reports both failures together when both paths fail.
Runtime builder cancellation disposes partially built nodes and links before
rethrowing cancellation.

## Composition Hosting

Namespace:

```text
FluxFlow.Composition.Hosting
```

Main types:

- `IApplicationDefinitionSource`
- `StaticApplicationDefinitionSource`
- `ConfigurationApplicationDefinitionSource`
- `ApplicationHostingBuilder`
- `ApplicationRevisionHostingOptions`
- `IApplicationRevisionHost`
- `ApplicationRevisionHost`
- `ApplicationRevisionLoadResult`
- `ApplicationRevisionHostState`
- `FluxFlowApplicationHostingServiceCollectionExtensions`
- `CompositionHostingOptions`
- `CompositionHostingBuilder`
- `ICompositionRuntimeHost`
- `CompositionRuntimeHost`
- `CompositionHostingException`
- `StaticCompositionDefinitionSource`
- `ConfigurationCompositionDefinitionSource`
- `ICompositionNodeRegistryContributor`
- `CompositionNodeFactoryContextResourceExtensions`
- `CompositionServiceProviderSnapshotBuilder`
- `CompositionServiceProviderSnapshot`
- `CompositionProviderBoundary`
- `CompositionProviderSnapshotInfo`
- `ApplicationRevisionCoordinator`
- `IApplicationRevisionCandidateFactory`
- `IApplicationRevisionCandidate`
- `ApplicationRevisionSnapshot`
- `ApplicationRevisionUpdateResult`
- `FluxFlowServiceCollectionExtensions`

Use `AddFluxFlowApplication(...)` when a .NET host wants DI to load the
canonical flat `ApplicationDefinition`, apply its initial revision, reload or
apply complete definitions, preserve an active revision after rejection, and
drain it at host stop. Candidate factories and revision event sinks are
registered explicitly. Source-load failures are stable degraded results rather
than .NET host failures, while cancellation remains cancellation. This layer
does not depend on Engine; the host-supplied candidate factory owns concrete
runtime preparation.
When the standard assembler contributes a registry, definitions are normalized
before planning. Update results expose migration diagnostics, and alias-only
updates are unchanged revisions.

`AddFluxFlowComposition(...)` and `ICompositionRuntimeHost` preserve the older
standalone `CompositionDefinition` host for existing consumers. Resource
helpers resolve named node resource references from keyed DI services; adapter
packages still own the resources.
Resource helper slot names and configured keyed service references are trimmed
before lookup so configuration whitespace does not change resource identity.
`CompositionHostingBuilder` supports direct delegate registration through
`RegisterNodes(...)` and explicit reusable contributor registration through
`RegisterNodeContributor<TContributor>()` or `RegisterNodeContributor(...)`;
it does not scan assemblies or discover node factories implicitly.
Hosted and manual lifecycle calls are idempotent at this boundary, so repeated
start or stop requests do not start or complete the same runtime more than
once. A stopped runtime is not restarted by the host.

Provider snapshot builders copy explicitly supplied service collections and
build normal Microsoft DI providers for `Host`, `ResourceRevision`, or
`WorkflowRevision` boundaries. Canonical address strings are keyed-service
keys for resources, components, typed input/output ports, and
`IFlowSignalTarget`. Factory registrations are provider-owned;
`...View` registrations are non-owning aliases; `AddExternal...`,
`BridgeExternal...`, and `CreateExternalHost(...)` keep the exact external
instance/provider externally owned. Snapshots do not scan, merge providers, or
perform fallback resolution. Scopes are available through the snapshot
provider but are never created per message implicitly.

The revision coordinator serializes complete-definition updates. Candidate
factories build replacements outside live routing; successful activation is
followed by one immutable current snapshot and old-candidate drain/disposal.
Preactivation failure preserves the old revision. Cleanup failures after commit
are reported without rolling back the new definition.

## Fluent DSL

Namespace:

```text
FluxFlow.Fluent
```

Main types:

- `Flow`
- `FlowBuilder<T>`
- `FlowTerminal`
- `FlowGraph`
- `FlowSegment<TIn, TOut>`
- `FlowSegment`

Use these types to compose standalone nodes in C# with compile-time-checked
wiring: `Flow.From(source).Then(node).To(sink).Build()`. The generic parameter
tracks the payload type between nodes, so `Then` only accepts a node whose input
matches the current output. `Tap` fans a payload to a side node without changing
the main line; `Branch` starts a typed sub-pipeline from a node's output port,
and passing the same node instance to more than one branch fans them in. The
built `FlowGraph` reuses the `FluxFlow.Composition` runtime for start, stop,
completion, aggregated errors/events, and disposal; each node completes once all
of its upstream sources finish, so fan-in drains before completing. `OnError` and
`OnEvent` (on the builder, terminal, or graph) observe the flow's aggregated
error/event streams; handlers are isolated and torn down with the graph.
`FlowSegment<TIn, TOut>` (via `FlowSegment.Define`) is a reusable named fragment
spliced into a chain with `Apply`; each application builds fresh nodes, so a
segment can be reused across graphs.

## Fluent DSL Hosting

Namespace:

```text
FluxFlow.Fluent.Hosting
```

Main types:

- `FluxFlowFluentHostingServiceCollectionExtensions`

Use `services.AddFlowGraph(sp => Flow.From(...)...Build())` to run a fluent
`FlowGraph` as an `IHostedService`: built and started when the host starts,
drained on host stop, and disposed on shutdown. The factory receives the
application `IServiceProvider`, so nodes can be resolved from DI. Call it more
than once to host several flows in one application.

## HTTP Composition

The outbound runtime package exposes `HttpClientNode` with
`FlowMessage<HttpClientRequest>` Input, `FlowMessage<HttpClientResult>` Output,
and Events. `HttpResponseResult` and `HttpClientFailureResult` share the one
normal result stream; exact request and response bodies use `FlowContent`.
Expected validation, timeout, transport, response-read, and configured status
failures do not require a separate Errors port.

Namespace:

```text
FluxFlow.Components.Http.Composition
```

Main types:

- `HttpComponentDesignMetadataProvider`
- `HttpCompositionNodeRegistryExtensions`
- `HttpCompositionNodeTypes`
- `HttpCompositionPortNames`
- `HttpCompositionResourceNames`

Use `RegisterHttpNodes()` from the optional
`FluxFlow.Components.Http.Composition` package when a composition host wants an
`http.request` node factory. The factory resolves a keyed `HttpClient` resource;
the host still owns client lifetime and transport policy. Invalid numeric
`HttpClientNodeOptions` values fail during build as factory diagnostics when the
host is configured to collect build failures.

`HttpComponentDesignMetadataProvider` exposes neutral Designer metadata for the
HTTP client composition node, including existing options, fixed ports, and
resource hints for the required `client` resource and optional `clock`
resource. `HttpClient` instances and clocks remain host-owned keyed resources.
The provider authors that metadata through the shared validated Designer
metadata builder.

## HTTP Trigger Adapter

Namespace:

```text
FluxFlow.Components.Http.AspNetCore
```

Main types:

- `FluxFlowHttpTriggerServiceCollectionExtensions`
- `FluxFlowTriggerEndpointExtensions`
- `HttpRequestContext`
- `HttpTriggerNode`
- `HttpTriggerSource`

Use `AddFluxFlowHttpTrigger(...)` and `MapFluxFlowTrigger(...)` when a web host
wants an inbound HTTP endpoint to feed a request/reply graph.
The adapter owns endpoint glue, keyed trigger source/node registration, and
hosted trigger lifetime. `MapFluxFlowTrigger(...)` rejects missing route
patterns before delegating to framework routing, and validates the keyed trigger
name or direct coordinator argument at the package boundary. The hosted lifetime
completes the keyed request source during stop so endpoint submissions are
rejected once the trigger is no longer consuming.

## Mapping Composition

Namespace:

```text
FluxFlow.Components.Mapping.Composition
```

Main types:

- `MappingCompositionNodeRegistryExtensions`
- `MappingComponentDesignMetadataProvider`
- `MappingCompositionNodeTypes`
- `MappingCompositionPortNames`
- `MappingCompositionResourceNames`

Use parameterless `RegisterMapper()` for the canonical `data.map` contract:
`FlowValue` input and one `FlowResult<FlowValue>` output. Expected expression
failures remain normal result data and retain the original value; the canonical
node has no `Failed` or universal error port. The factory resolves a keyed
`IFlowExpressionEngine`; optional keyed context factory and clock resources stay
host-owned. Mapping Composition 3.x exposes no generic CLR registration;
convert CLR values explicitly at the application boundary.

`MappingComponentDesignMetadataProvider` exposes neutral Designer metadata for
the `data.map` composition node so hosts can compose palette, editor,
validation, or documentation hints without copying package descriptors. The
metadata includes editable options with section/editor hints, canonical
`FlowValue`/`FlowResult<FlowValue>` ports, and host-owned resource picker hints
using `Resources.{name}` for the required `engine` resource plus optional
`contextFactory` and `clock` resources.

## Assertions Composition

Namespace:

```text
FluxFlow.Components.Assertions.Composition
```

Main types:

- `AssertionsCompositionNodeRegistryExtensions`
- `AssertionsComponentDesignMetadataProvider`
- `AssertionsCompositionNodeTypes`
- `AssertionsCompositionPortNames`
- `AssertionsCompositionResourceNames`

Use parameterless `RegisterAssertion()` from the optional
`FluxFlow.Components.Assertions.Composition` package for the canonical fixed
`data.assert` factory. It consumes `FlowValue` and emits one
`FlowResult<FlowValueAssertionResult>` output. Passed and failed rules are
normal successful result kinds; missing input and expression evaluation
failures remain normal error results. The factory resolves a required keyed
`IFlowExpressionEngine`; optional keyed `IFlowMapContextFactory<FlowValue>` and
clock resources stay host-owned. Convert CLR inputs explicitly at the
application boundary and replace prior Passed, Failed, and Errors links with
conditions over `FlowResult.Kind`, `IsError`, and `Error.Code`.

`AssertionsComponentDesignMetadataProvider` exposes neutral Designer metadata
for the `data.assert` composition node so hosts can compose palette, editor,
validation, or documentation hints without copying package descriptors. The
metadata includes editable options with section/editor hints, canonical
`FlowValue`/`FlowResult<FlowValueAssertionResult>` ports, and host-owned resource
picker hints using `Resources.{name}` for the required `engine` resource plus
optional `contextFactory` and `clock` resources. Engine selection is represented
only by the required resource, without a duplicate string option.

## Control Composition

Namespace:

```text
FluxFlow.Components.Control.Composition
```

Main types:

- `ControlCompositionNodeRegistryExtensions`
- `ControlComponentDesignMetadataProvider`
- `ControlCompositionNodeTypes`
- `ControlCompositionPortNames`
- `ControlCompositionResourceNames`

`RegisterFilter<TInput>()` and `RegisterWhen<TInput>()` are compatibility
factories and are obsolete in Control Composition `2.x`. Canonical definitions
represent filtering as one conditioned output link and branching as
complementary conditioned output links. Composition compiles conditions once,
isolates a failed condition to its link, and preserves sibling fan-out without
requiring a structural control node.

Existing definitions can retain the factories. They still resolve a keyed
`IFlowExpressionEngine`; optional keyed typed context factory and clock
resources remain host-owned, and invalid legacy options continue to surface as
factory diagnostics.

`ControlComponentDesignMetadataProvider` preserves complete neutral metadata
for existing `flow.filter` and `flow.when` documents, including editable
options, ports, aliases, and host-owned resources. Both entries are marked
deprecated with canonical-link migration guidance; hosts can hide them from
new-node palettes while continuing to render and validate stored definitions.

## Validation Composition

Namespace:

```text
FluxFlow.Components.Validation.Composition
```

Main types:

- `ValidationCompositionNodeRegistryExtensions`
- `ValidationComponentDesignMetadataProvider`
- `ValidationCompositionNodeTypes`
- `ValidationCompositionPortNames`
- `ValidationCompositionResourceNames`

Use parameterless `RegisterJsonSchemaValidator()` from the optional
`FluxFlow.Components.Validation.Composition` package for the canonical fixed
`json.validate` factory. It consumes `FlowValue` and emits one
`FlowResult<JsonSchemaFlowValueValidationResult>` output. Valid and invalid
schema outcomes are normal successful result kinds; selector or evaluation
failures remain normal error results. The factory binds
`JsonSchemaValidatorOptions`, compiles inline `schema` or `schemaPath` during
composition build, and resolves optional host-owned
`IJsonSchemaFlowValueSelector` and clock resources through exact
`Resources.{name}` addresses.
Invalid validator options fail during build as factory diagnostics when the host
is configured to collect build failures.

Validation now has one maintained application contract. Convert CLR values to
`FlowValue` at the application boundary, register the parameterless canonical
factory, and replace prior Valid, Invalid, and Errors links with conditions over
`FlowResult.Kind`, `IsError`, and `Error.Code`. The removed
`payloadSelector` alias maps directly to `valueSelector`.

`ValidationComponentDesignMetadataProvider` exposes neutral Designer metadata
for the `json.validate` composition node so hosts can compose palette,
editor, validation, or documentation hints without copying package descriptors.
The metadata includes editable options, the fixed FlowValue/single-result port
pair, and resource hints for the optional `selector` and `clock` resources.

## Timers Composition

Namespace:

```text
FluxFlow.Components.Timers.Composition
```

Main types:

- `TimersComponentDesignMetadataProvider`
- `TimersCompositionNodeRegistryExtensions`
- `TimersCompositionNodeTypes`
- `TimersCompositionPortNames`
- `TimersCompositionResourceNames`

Use `RegisterTimerInterval()`, `RegisterTimerSchedule()`,
`RegisterTimerDelay<TInput>()`, `RegisterTimerThrottle<TInput>()`, and
`RegisterTimerDebounce<TInput>()` from the optional
`FluxFlow.Components.Timers.Composition` package when a composition host wants
timer source and transform node factories. The factories bind existing timer
settings and resolve optional keyed `TimeProvider` resources through the host.
Invalid timer settings fail during build as factory diagnostics when the host is
configured to collect build failures.

`TimersComponentDesignMetadataProvider` exposes neutral Designer metadata for
the five timer composition nodes so hosts can compose palette, editor,
validation, or documentation hints without copying package descriptors. The
metadata includes editable options, fixed ports, and a resource hint for the
optional `clock` resource. It does not add schedule time-zone string
conversion; schedule metadata declares `timeZone` as an omitted editable option
because that setting requires typed `TimeZoneInfo` configuration.

## Sources Composition

Namespace:

```text
FluxFlow.Components.Sources.Composition
```

Main types:

- `SourcesComponentDesignMetadataProvider`
- `SourcesCompositionNodeRegistryExtensions`
- `SourcesTypedRegistrationExtensions`
- `SourcesCompositionNodeTypes`
- `SourcesCompositionPortNames`
- `SourcesCompositionResourceNames`

Use parameterless `RegisterGeneratedSource()` and `RegisterSequenceSource()`
from the optional `FluxFlow.Components.Sources.Composition` package for the
canonical fixed contracts. Both are zero-input sources with one `FlowValue`
Output, Events, and no universal Errors port. Generated `items` accepts one
ordinary JSON value or an array; each item is decoded once into immutable
FlowValue data during activation. Both factories resolve an optional exact
keyed `TimeProvider` through the host.
Invalid source option values fail during composition build through the factory
path, so hosts that collect build diagnostics receive `FactoryFailed` entries
instead of a partially created runtime.

Explicit `RegisterGeneratedSource<TOutput>(nodeType)` and
`RegisterSequenceItemSource(nodeType)` calls preserve the released typed
outputs for code-authored compatibility. Use distinct node type names when
typed and canonical registrations share a registry.

`SourcesComponentDesignMetadataProvider` exposes neutral Designer metadata for
generated and sequence source composition nodes so hosts can compose palette,
editor, validation, or documentation hints without copying package descriptors.
The metadata includes inline generated `items` as JSON node configuration,
canonical fixed FlowValue output ports, and a resource hint for the optional
`clock` resource. The generic-only `outputType` diagnostic option is explicitly
omitted from the canonical metadata.
The provider authors that metadata through the shared validated Designer
metadata builder.

## Observability Composition

Namespace:

```text
FluxFlow.Components.Observability.Composition
```

Main types:

- `ObservabilityComponentDesignMetadataProvider`
- `ObservabilityCompositionNodeRegistryExtensions`
- `ObservabilityCompositionNodeTypes`
- `ObservabilityCompositionPortNames`
- `ObservabilityCompositionResourceNames`

Use parameterless `RegisterCounter()`, `RegisterLogger()`, and
`RegisterMetrics()` from the optional
`FluxFlow.Components.Observability.Composition` package for the canonical
FlowValue contracts. Counter results distinguish counted and predicate-rejected
values. Logger and Metrics selector failures are one partial error result that
carries the usable log entry or metric snapshot. Every descriptor has one
normal FlowResult Output, Events, and no universal Errors port.

Explicit `RegisterCounter<TInput>()`, `RegisterLogger<TInput>()`, and
`RegisterMetrics<TInput>()` overloads preserve the released generic direct
Output and Errors contracts for code-authored compatibility. All factories
resolve host-owned keyed expression, selector, context, and clock resources.
Invalid options fail during build as factory diagnostics when the host collects
build failures.

`ObservabilityComponentDesignMetadataProvider` exposes neutral Designer metadata
for the three canonical observability composition nodes, including FlowValue
options, fixed result ports, and host-owned resource hints. Counter metadata includes the
conditionally required expression engine plus optional context factory and
clock resources. Logger metadata includes the dynamic `attribute:{name}`
FlowValue selector resource pattern, and metrics metadata includes the optional
FlowValue `sizeSelector` and `clock` resources. Expression engines, context
factories, selectors, and clocks remain host-owned keyed resources. The provider
authors that metadata through the shared validated Designer metadata builder.

## Metrics Composition

Namespace:

```text
FluxFlow.Components.Metrics.Composition
```

Main types:

- `MetricsComponentDesignMetadataProvider`
- `MetricsCompositionNodeRegistryExtensions`
- `MetricsCompositionNodeTypes`
- `MetricsCompositionPortNames`
- `MetricsCompositionResourceNames`

Use `RegisterMetricsAggregate()` from the optional
`FluxFlow.Components.Metrics.Composition` package when a composition host wants
a canonical `metric.aggregate` node factory. It consumes typed
`MetricSampleInput` values and emits successful snapshots, partial group-limit
applications, and expected failures through one
`FlowResult<MetricSnapshotOutput>` Output. The descriptor has no universal
Errors port. The factory binds `MetricsAggregateOptions` and can resolve an
optional keyed `TimeProvider` resource through the host.

Runtime package 5.0 consolidates this contract on `MetricsAggregateNode` and
removes the 4.x direct snapshot Output, Errors port, and temporary
`FlowMetricsAggregateNode` name. Code-authored consumers inspect the normal
result and read its optional snapshot Value.

`MetricsComponentDesignMetadataProvider` exposes neutral Designer metadata for
the `metric.aggregate` composition node, including existing metrics aggregate
options, canonical fixed ports, and a resource hint for the optional `clock`
resource. The provider authors that metadata through the shared validated
Designer metadata builder.

## Routing Composition

Namespace:

```text
FluxFlow.Components.Routing.Composition
```

Main types:

- `RoutingComponentDesignMetadataProvider`
- `RoutingCompositionNodeRegistryExtensions`
- `RoutingCompositionNodeTypes`
- `RoutingCompositionPortNames`
- `RoutingCompositionResourceNames`

Use parameterless `RegisterWindow()`, `RegisterCorrelation()`, and
`RegisterJoin()` from the optional
`FluxFlow.Components.Routing.Composition` package for the canonical fixed
contracts. They consume `FlowValue` and emit one `FlowResult<T>` Output;
Correlation and Join resolve host-owned keyed `Func<FlowValue,string?>`
selectors, and all three can resolve an optional keyed `TimeProvider`.
Expected operation failures remain normal result data and canonical descriptors
do not expose a universal Errors port. Invalid options and missing resources
fail during build as factory diagnostics when the host collects build failures.

Explicit `RegisterWindow<TInput>()`, `RegisterCorrelation<TInput>()`, and
`RegisterJoin<TLeft,TRight>()` overloads preserve the released typed contracts
under host-selected node type names. `RegisterSwitch<TInput>()`,
`RegisterFork<TInput>()`, and `RegisterMerge<TInput>()` remain available but are
obsolete because canonical links provide conditional routing, fan-out, and
shared-input fan-in.

`RoutingComponentDesignMetadataProvider` exposes neutral Designer metadata for
the six routing composition types so hosts can compose palette, editor,
validation, or documentation hints without copying package descriptors. The
retained nodes use canonical FlowValue/result ports; structural nodes are marked
deprecated while preserving their option-defined dynamic output metadata. The
provider also describes host-owned resource hints for selector delegates and
`clock`.
The provider authors that metadata through the shared validated Designer
metadata builder, including built-in input and output port descriptors.

## Serialization Composition

Namespace:

```text
FluxFlow.Components.Serialization.Composition
```

Main types:

- `SerializationComponentDesignMetadataProvider`
- `SerializationCompositionNodeRegistryExtensions`
- `SerializationCompositionNodeTypes`
- `SerializationCompositionPortNames`
- `SerializationCompositionResourceNames`

Use `RegisterJsonParse()`, `RegisterJsonStringify()`,
`RegisterTextEncode()`, `RegisterTextDecode()`, `RegisterBase64Encode()`, and
`RegisterBase64Decode()` from the optional
`FluxFlow.Components.Serialization.Composition` package when a composition host
wants canonical serialization and encoding factories. Parse/decode registrations
convert `FlowContent` to `FlowResult<FlowValue>`; stringify/encode registrations
convert `FlowValue` to `FlowResult<FlowContent>`. Expected conversion failures
stay on the normal output. The factories bind `SerializationNodeOptions` and can
resolve an optional keyed `TimeProvider` resource through the host.

`SerializationComponentDesignMetadataProvider` exposes neutral Designer
metadata for the six serialization composition nodes so hosts can compose
palette, editor, validation, or documentation hints without copying package
descriptors. The metadata includes shared options, fixed ports, and a resource
hint for the optional `clock` resource using the exact `Resources.{name}` address
pattern. The request-based standalone nodes remain available from the runtime
package for code-authored compatibility.
The provider authors that metadata through the shared validated Designer
metadata builder.

## Payloads Composition

Namespace:

```text
FluxFlow.Components.Payloads.Composition
```

Main types:

- `PayloadsComponentDesignMetadataProvider`
- `PayloadsCompositionNodeRegistryExtensions`
- `PayloadsCompositionNodeTypes`
- `PayloadsCompositionPortNames`
- `PayloadsCompositionResourceNames`

Use `RegisterPayloadInspect()` from the optional
`FluxFlow.Components.Payloads.Composition` package when a composition host wants
a canonical `payload.inspect` node factory. The factory binds existing
`PayloadInspectOptions`, consumes `FlowContent`, emits
`FlowResult<PayloadInspectionResult>` through one normal output, and can resolve
optional keyed `FlowContentCodecCatalog` and `TimeProvider` resources through
the host. The request-based `PayloadInspectNode` remains available from the
runtime package for code-authored compatibility.

`PayloadsComponentDesignMetadataProvider` exposes neutral Designer metadata for
the `payload.inspect` composition node so hosts can compose palette, editor,
validation, or documentation hints without copying package descriptors. The
metadata includes options, canonical fixed ports, and host-owned picker hints
for the optional `codecs` and `clock` resources.
The provider authors that metadata through the shared validated Designer
metadata builder.

## FileSystem Composition

Namespace:

```text
FluxFlow.Components.FileSystem.Composition
```

Main types:

- `FileSystemComponentDesignMetadataProvider`
- `FileSystemCompositionNodeRegistryExtensions`
- `FileSystemCompositionNodeTypes`
- `FileSystemCompositionPortNames`
- `FileSystemCompositionResourceNames`

Use `RegisterFileRead()`, `RegisterFileWrite()`,
`RegisterDirectoryEnumerate()`, and `RegisterFileWatch()` from the optional
`FluxFlow.Components.FileSystem.Composition` package when a composition host
wants file-system node factories. The factories bind existing file-system
options and can resolve an optional keyed `TimeProvider` resource through the
host.
Invalid file-system option values fail during composition build through the
factory path, so hosts that collect build diagnostics receive `FactoryFailed`
entries instead of a partially created runtime.

`FileSystemComponentDesignMetadataProvider` exposes neutral Designer metadata
for the four file-system composition nodes so hosts can compose palette,
editor, validation, or documentation hints without copying package descriptors.
The metadata keeps path policy as node configuration and includes a resource
hint for the optional `clock` resource. The provider authors that metadata
through the shared validated Designer metadata builder.

## State Composition

Namespace:

```text
FluxFlow.Components.State.Composition
```

Main types:

- `StateCompositionNodeRegistryExtensions`
- `StateCompositionNodeTypes`
- `StateCompositionPortNames`
- `StateCompositionResourceNames`
- `StateComponentDesignMetadataProvider`

Use `RegisterStateReducer()` from the optional
`FluxFlow.Components.State.Composition` package when a composition host wants a
`state.reduce` node factory. The canonical factory consumes
`FlowValueStateReducerInput` and emits one
`FlowResult<FlowValueStateReducerResult>` Output plus Events. Updated, reset,
and cleared operations are successful result variants; expected key,
expression, reducer, and key-limit failures are normal error variants. The
descriptor has no universal Errors port.

The factory binds ordinary JSON `initialState` values into immutable
`FlowValue`, resolves a required keyed `IFlowExpressionEngine`, and can resolve
an optional keyed `TimeProvider` through exact host-owned resource addresses.
State `5.x` has one maintained `FlowValueStateReducerNode` contract; CLR values
are converted explicitly at the application boundary. Composition `3.x`
registers only that canonical fixed contract.

`StateComponentDesignMetadataProvider` exposes neutral Designer metadata for
`state.reduce`, including canonical reducer options and fixed ports, and
resource hints for the required `engine` resource plus optional `clock`
resource. Both use canonical `Resources.{name}` picker addresses, and the
resource reference is the only engine-selection contract. The provider authors
that metadata through the shared validated Designer metadata builder.

## Storage Composition

Namespace:

```text
FluxFlow.Components.Storage.Composition
```

Main types:

- `StorageCompositionNodeRegistryExtensions`
- `StorageCompositionNodeTypes`
- `StorageCompositionPortNames`
- `StorageCompositionResourceNames`
- `StorageComponentDesignMetadataProvider`

Use `RegisterStoragePut()`, `RegisterStorageGet()`,
`RegisterStorageQuery()`, and `RegisterStorageDelete()` from the optional
`FluxFlow.Components.Storage.Composition` package when a composition host wants
storage node factories. Each descriptor has one Input, one normal
`FlowResult<T>` Output, Events, and no universal Errors or operation-specific
branch ports. The factories bind existing storage options, resolve a
required keyed `IStorageStore` or `IStorageStoreFactory`, and can resolve an
optional keyed `TimeProvider` resource through the host. Factory resources are
opened during composition build and released with composed node disposal; direct
stores remain host-owned.

`StorageComponentDesignMetadataProvider` exposes neutral Designer metadata for
the four storage composition nodes, including existing storage options and fixed
ports, plus resource hints for the required `store` resource and optional
`clock` resource. The `store` resource may point at either a keyed
`IStorageStore` or keyed `IStorageStoreFactory`. The provider authors that
metadata through the shared validated Designer metadata builder.

Storage 5.x uses the concise `StoragePutNode`, `StorageGetNode`,
`StorageQueryNode`, and `StorageDeleteNode` names for exact `FlowContent`
operations. The 3.x Composition adapter removes the former typed compatibility
registrations. Store/factory request, record, and result contracts remain the
stable boundary implemented by concrete backend packages.

## Sessions Composition

Namespace:

```text
FluxFlow.Components.Sessions.Composition
```

Main types:

- `SessionsCompositionNodeRegistryExtensions`
- `SessionsCompositionNodeTypes`
- `SessionsCompositionPortNames`
- `SessionsCompositionResourceNames`
- `SessionsComponentDesignMetadataProvider`

Related base Sessions types:

- `SessionStoreServiceCollectionExtensions`
- `ISessionStore`
- `ISessionStoreFactory`
- `SessionStoreContext`
- `SessionStoreLease`
- `SessionRecordInput`
- `SessionRecord`
- `SessionContentRecordInput`
- `SessionContentRecord`
- `SessionQueryOutcome`

Use `RegisterSessionRecorder()`, `RegisterSessionReplay()`, and
`RegisterSessionQuery()` from the optional
`FluxFlow.Components.Sessions.Composition` package when a composition host wants
session node factories. The factories bind existing session options, resolve a
required keyed `ISessionStore` or `ISessionStoreFactory`, and can resolve an
optional keyed `TimeProvider` resource through the host. Factory resources are
opened during composition build and released with composed node disposal; direct
stores remain host-owned.
Invalid session option values fail during composition build through the factory
path, so hosts that collect build diagnostics receive `FactoryFailed` entries
instead of a partially created runtime.

Sessions `5.x` exposes one maintained node set: `SessionRecorderNode` accepts
`SessionContentRecordInput`, `SessionReplayNode` emits exact-content records as
a source, and `SessionQueryNode` accepts `SessionQueryRequest`. Their successful
and expected failure outcomes use one `FlowResult<T>` Output plus Events; there
is no typed-node compatibility layer, query branch, numeric error-code surface,
or universal Errors port. `SessionRecordInput` and `SessionRecord` remain the
stable object-valued store adapter boundary rather than alternate node ports.

`SessionsComponentDesignMetadataProvider` exposes neutral Designer metadata for
the three session composition nodes, including existing session options and fixed
ports, plus resource hints for the required `store` resource and optional
`clock` resource. The `store` resource may point at either a keyed
`ISessionStore` or keyed `ISessionStoreFactory`; it is the only store selector.
Both picker patterns use exact `Resources.{name}` addresses. The provider
authors that metadata through the shared validated Designer metadata builder.

The base Sessions package owns the neutral store contracts, factory, context,
lease, and keyed DI registration helpers used by direct hosts and composition
adapters; it still does not own any concrete persistence backend.

## Projections Composition

Namespace:

```text
FluxFlow.Components.Projections.Composition
```

Main types:

- `ProjectionsComponentDesignMetadataProvider`
- `ProjectionsCompositionNodeRegistryExtensions`
- `ProjectionsCompositionNodeTypes`
- `ProjectionsCompositionPortNames`
- `ProjectionsCompositionResourceNames`

Use `RegisterEventProjection()` from the optional
`FluxFlow.Components.Projections.Composition` package when a composition host
wants the canonical `event.project` node factory. It consumes typed
`ProjectionEvent` values and emits matching and final snapshots through one
`FlowResult<EventProjectionSnapshot>` Output. Expected projection failures are
normal error variants; the descriptor has no universal Errors port. The
factory binds `EventProjectionOptions` and can resolve an optional keyed
`TimeProvider` through an exact host-owned resource address. A configured final
snapshot is emitted after accepted input drains during normal completion.

Runtime package 5.0 consolidates this contract on `EventProjectionNode` and
removes the 4.x direct snapshot Output, Errors port, and temporary
`FlowEventProjectionNode` name. The explicit final-flush helper remains and
waits for the same canonical completion lifecycle.

`ProjectionsComponentDesignMetadataProvider` exposes neutral Designer metadata
for the `event.project` composition node, including existing projection
options, fixed ports, and a resource hint for the optional `clock` resource.
The provider authors that metadata through the shared validated Designer
metadata builder.

## Expectations Composition

Namespace:

```text
FluxFlow.Components.Expectations.Composition
```

Main types:

- `ExpectationsComponentDesignMetadataProvider`
- `ExpectationsCompositionNodeRegistryExtensions`
- `ExpectationsCompositionNodeTypes`
- `ExpectationsCompositionPortNames`
- `ExpectationsCompositionResourceNames`

Use `RegisterEventExpectation()` from the optional
`FluxFlow.Components.Expectations.Composition` package when a composition host
wants the canonical `event.expect` node factory. It consumes
`ProjectionEvent` and emits one `FlowResult<EventExpectationResult>` Output.
Matched and unmet rules, timeout, and ordered input completion are normal
successful variants; expected filter evaluation failure is a normal error
variant. The factory binds `EventExpectationOptions` and can resolve an optional
host-owned keyed `TimeProvider` through an exact `Resources.{name}` address.

Expectations `5.x` has one maintained `EventExpectationNode` contract. Its
matched, unmet, timeout, completion, and expected evaluation-failure outcomes
all use the normal `FlowResult<EventExpectationResult>` Output; there is no
direct-result compatibility node, numeric error code surface, or universal
Errors port. Expectations Composition `3.x` registers that fixed contract.

`ExpectationsComponentDesignMetadataProvider` exposes neutral Designer metadata
for the canonical `event.expect` composition node, including existing
expectation options, `ProjectionEvent`/`FlowResult<EventExpectationResult>`
fixed ports, and a canonical `Resources.{name}` picker hint for the optional
host-owned `clock` resource. The provider authors that metadata through the
shared validated Designer metadata builder. Typed result values are never
implicitly unwrapped by links.

## MQTT Core

Namespace:

```text
FluxFlow.Components.Mqtt
FluxFlow.Components.Mqtt.Acknowledgements
FluxFlow.Components.Mqtt.Client
FluxFlow.Components.Mqtt.Configuration
FluxFlow.Components.Mqtt.Contracts
FluxFlow.Components.Mqtt.Events
FluxFlow.Components.Mqtt.Nodes
FluxFlow.Components.Mqtt.Options
FluxFlow.Components.Mqtt.Subscriptions
FluxFlow.Components.Mqtt.Transport
```

Main types:

- `MqttClientController`
- `IMqttClientController`
- `MqttClientConfiguration`
- `MqttBrokerConfiguration`
- `MqttReconnectConfiguration`
- `MqttRetryPolicy`
- `MqttClientRequest`
- `MqttClientResult`
- `MqttControlNode`
- `MqttPublishOperationNode`
- `MqttSubscriptionTriggerNode`
- `MqttClientEventsNode`
- `MqttPublishMessage`
- `MqttReceivedApplicationMessage`
- `MqttSubscriptionDefinition`
- `MqttSubscriptionTarget`
- `IMqttTransportFactory`
- `IMqttTransportSession`
- `MqttTransportCapabilities`
- `MqttWorkflowAcknowledgement`
- `MqttBrokerAcknowledgement`

Legacy migration types retained in 5.x:

- `IMqttPublisher`
- `IMqttTriggerSource`
- `IMqttSubscription`
- `IMqttReceivedContext`
- `IMqttClientHealthSource`
- `MqttPublishNode`
- `MqttTriggerNode`
- `MqttPublishRequest`
- `MqttPublishResult`
- `MqttPublishProperties`
- `MqttReceivedMessage`
- `MqttTriggerOptions`
- `MqttTriggerResponse`
- `MqttClientHealthEvent`
- `MqttTopicValidator`

Use `FluxFlow.Components.Mqtt` 5.x when a host wants one transport-neutral,
host-lifetime controller per logical MQTT client and standalone control,
focused publish, trigger, and domain-event components. Expected operation
failures are `MqttClientResult` variants on normal output. MQTT payloads use
`FlowContent`; Ack/Nak signal payloads are ignored and matched by `TraceId`.
Concrete MQTT clients stay behind `IMqttTransportFactory` and
`IMqttTransportSession`.

The previous publisher/trigger-source contracts remain available only for
coordinated adapter and Composition migration. Their existing immutable-copy
behavior remains unchanged:
`MqttPublishProperties.UserProperties`,
`MqttReceivedMessage.UserProperties`, and
`MqttClientHealthEvent.Attributes` snapshot assigned dictionaries with ordinal
key comparison, and treat null maps as empty. `MqttPublishRequest.Payload`,
`MqttReceivedMessage.Payload`, and `MqttReceivedMessage.CorrelationData`
snapshot assigned byte arrays while preserving the existing byte-array public
contract.

## MQTT Composition

Namespace:

```text
FluxFlow.Components.Mqtt.Composition
```

Main types:

- `MqttComponentDesignMetadataProvider`
- `MqttCompositionNodeRegistryExtensions`
- `MqttCompositionNodeTypes`
- `MqttCompositionPortNames`
- `MqttCompositionResourceNames`

Use `RegisterMqttNodes()` from the optional
`FluxFlow.Components.Mqtt.Composition` package when a composition host wants
`mqtt.publish` and `mqtt.receive` node factories. The factories resolve keyed
`IMqttPublisher` and `IMqttTriggerSource` resources; concrete MQTT adapters or
the host still own broker/client registration. MQTT adapter registration
helpers reject invalid service/key/options arguments and null options factory
results before creating keyed client sessions. At the standalone node layer,
`MqttTriggerNode` reports malformed received contexts as trigger errors without
stopping later valid subscription messages.

`MqttComponentDesignMetadataProvider` exposes neutral Designer metadata for the
MQTT publish and trigger composition nodes, including existing options, fixed
ports, and resource hints for `publisher`, `triggerSource`, and optional
`clock` resources. Publisher, trigger source, and clock resources remain
host-owned. The provider authors that metadata through the shared validated
Designer metadata builder.

## MQTTnet Adapter

Namespace:

```text
FluxFlow.Components.Mqtt.MqttNet
```

Main types:

- `FluxFlowMqttServiceCollectionExtensions`
- `MqttClientRegistrationOptions`
- `MqttNetClient`
- `MqttNetClientOptions`
- `MqttNetLastWillOptions`
- `MqttNetMessageMapper`
- `MqttNetReceivedContext`
- `MqttNetSubscription`
- `MqttNetTopicMatcher`

`FluxFlow.Components.Mqtt.MqttNet` is the MQTTnet-backed adapter package for
the neutral MQTT contracts. `MqttNetClient` implements `IMqttPublisher`,
`IMqttTriggerSource`, and `IMqttClientHealthSource`; it owns MQTTnet client
creation, broker connection, reconnect behavior, Last Will setup, publish
mapping, trigger subscriptions, acknowledgement, and health events.

`AddFluxFlowMqttClient()` registers one keyed `MqttNetClient` and exposes the
same singleton through keyed MQTT publisher, trigger-source, and health-source
contracts. Registration owns only the adapter client session. Workflow nodes
are still created through standalone composition, and the host decides whether
the adapter connects with hosted lifetime through `ConnectWithHost`.
`MqttNetClientOptions.UserProperties` snapshots assigned dictionaries with
ordinal key comparison, and treats null maps as empty.
`MqttNetLastWillOptions.Payload` snapshots assigned byte arrays, and adapter
publish/Last Will mapping copies payload buffers before concrete client handoff.

## Pulse MQTT Adapter

Namespace:

```text
FluxFlow.Components.Mqtt.PulseMqtt
```

Main types:

- `FluxFlowMqttServiceCollectionExtensions`
- `MqttClientRegistrationOptions`
- `PulseMqttClient`
- `PulseMqttClientOptions`
- `PulseMqttLastWillOptions`
- `PulseMqttMessageMapper`
- `PulseMqttReceivedContext`
- `PulseMqttSubscription`
- `RejectingMessageStore`

`FluxFlow.Components.Mqtt.PulseMqtt` is the Pulse MQTT-backed adapter package
for the neutral MQTT contracts. `PulseMqttClient` implements `IMqttPublisher`,
`IMqttTriggerSource`, and `IMqttClientHealthSource`; it owns Pulse client
creation, transport configuration, resilient start/stop, broker connection,
Last Will setup, publish mapping, trigger subscriptions, acknowledgement, and
health events.

The adapter keeps FluxFlow publish behavior strict by default: publishing while
disconnected fails unless the host explicitly enables
`AllowOfflinePublishQueue`. Durable message and session stores are
adapter-owned options on `PulseMqttClientOptions`, not core MQTT or composition
features. `AddFluxFlowMqttClient()` registers one keyed client session and can
optionally add hosted lifecycle through `StartWithHost`; `WaitForConnectedOnStart`
is only valid with hosted start.
`PulseMqttClientOptions.UserProperties` snapshots assigned dictionaries with
ordinal key comparison, and treats null maps as empty.
`PulseMqttLastWillOptions.Payload` snapshots assigned byte arrays, and adapter
publish/Last Will mapping copies payload buffers before concrete client handoff.

## Designer Metadata

Namespace:

```text
FluxFlow.Components.Designer
FluxFlow.Components.Designer.Contracts
```

Main types:

- `ComponentType`
- `ComponentCategory`
- `ComponentIconKey`
- `ComponentPreferredNodeName`
- `ComponentOptionName`
- `ComponentOptionChoiceValue`
- `ComponentResourceName`
- `ComponentPortName`
- `ComponentPortGroup`
- `ComponentAttributeName`
- `ComponentAttributeValue`
- `ComponentMetadataText`
- `ComponentValueTypeHint`
- `ComponentDesignMetadata`
- `OptionDesignMetadata`
- `OptionChoiceMetadata`
- `OptionValueKind`
- `OptionDesignMetadataAttributeNames`
- `OptionDesignMetadataAttributeValues`
- `OptionDesignMetadataAttributes`
- `ResourceDesignMetadata`
- `ComponentResourcePickerHint`
- `ComponentResourcePickerHints`
- `PortDesignMetadata`
- `PortDirection`
- `IComponentDesignMetadataProvider`
- `ComponentDesignMetadataBuilder`
- `ComponentDesignMetadataCatalog`
- `ComponentDesignMetadataModule`
- `ComponentDesignMetadataServiceCollectionExtensions`
- `ComponentDesignMetadataValidator`
- `DesignerMetadataValidationError`
- `ResourceDesignMetadataAttributeNames`
- `ResourceDesignMetadataAttributeValues`
- `ResourceDesignMetadataAttributes`

Use these types when reusable packages want to describe neutral palette,
editor, validation, and generated-doc metadata without depending on either the
composition runtime or the engine runtime.
`ComponentType`, `ComponentCategory`, `ComponentIconKey`,
`ComponentPreferredNodeName`, `ComponentOptionName`,
`ComponentOptionChoiceValue`, `ComponentResourceName`, `ComponentPortName`, and
`ComponentPortGroup`, `ComponentAttributeName`, `ComponentAttributeValue`,
`ComponentMetadataText`, and `ComponentValueTypeHint` are Designer-owned value
types, keeping
component, category, icon, preferred node name, option, option-choice, resource,
port, port-group, metadata attribute-key, metadata attribute-value, metadata
display text, and value type hint contracts independent from engine definition
contracts.

`ComponentDesignMetadataValidator` enforces identifier, option, choice,
resource, port, and attribute consistency. Enum options must define choices,
choice lists are valid only on enum options, option defaults must match their
declared kind, and min/max constraints are limited to number and duration
options.
`ComponentDesignMetadataCatalog` validates and snapshots registered metadata so
caller-owned option, resource, port, choice, and typed attribute collections
cannot mutate catalog contents after registration. Canonical catalog projection
adds the traced `Events` output and optional semantic `processing` profile
resource, while omitting legacy `name` and Dataflow-specific options from normal
editing.
`ComponentDesignMetadataBuilder` is an authoring helper over the same contracts;
it supports single and bulk component-level attributes through `AddAttribute`
and `AddAttributes`, validates and snapshots raw provider metadata, and does not
own rendering, localization, resource selection, or runtime mapping. Canonical
host projection occurs only when metadata is added to a catalog.
`OptionDesignMetadataAttributes` provides shared option attribute helpers so
package metadata can declare section, importance, editor, syntax, and
related-resource hints without owning host rendering or editor behavior.
`ResourceDesignMetadataAttributes` provides shared host-owned resource
attribute helpers so package metadata can declare resource ownership, picker
kind, key pattern, related option, and conditional requiredness without owning
the host resource catalog.
`ComponentResourcePickerHints` reads those existing host-owned resource
attributes from one metadata item or a catalog and returns ordered
`ComponentResourcePickerHint` values for host resource-picker integrations. It
does not render controls, enumerate resource instances, resolve keyed services,
or own resource lifetimes.
`DesignerApplicationPersistence` normalizes registered component and resource
aliases on load and save. Load results include structured migration diagnostics;
serialization emits canonical names.
`ComponentDesignMetadataServiceCollectionExtensions` registers package-owned
metadata providers and a singleton validated catalog in host DI, while leaving
palette rendering, localization, and resource pickers owned by the host.

## Support Packages

These packages are intentionally not standalone node composition adapters:

- `FluxFlow.Components.Configuration` validates resource and secret references
  through canonical nested `ApplicationAddress` resource identities. Its fluent
  builder accepts application addresses directly, and runtime or descriptor-only
  validation reports missing declarations, kind/version mismatches, invalid
  ownership, and malformed option metadata without opening resources during
  descriptor-only checks.
- `FluxFlow.Components.Resources` defines canonical resource names,
  references, descriptor catalogs, lookup diagnostics, and required `Host`,
  `ResourceRevision`, or `External` ownership. Keyed registration uses exact
  `ApplicationAddress.Value` identities and separates provider-owned factories
  from non-owning external bridges.
- `FluxFlow.Components.Secrets` uses the same resource address and ownership
  model for secret references and non-sensitive descriptors. It retains
  version/kind matching, option resolution, redacted values, and local resolver
  authoring while distinguishing provider-owned resolvers from external
  bridges.
- `FluxFlow.Components.Expressions` provides expression engine and context
  factory registries used by adapters that resolve host-owned expression
  services, explicit expression registry argument guards, deterministic
  most-specific context factory lookup, and keyed DI registration helpers for
  host-owned expression engines and typed map context factories.
- `FluxFlow.Components.Journal` provides runtime-neutral journal event input,
  fluent event input authoring, record mapping, store contracts, store
  factory/context/lease helpers, keyed DI registration helpers, retention
  option validation, and named in-memory store factory support for hosts.
  Its keyed registration helpers reject invalid service/key/provider arguments
  and null provider results before creating keyed store resources.
- `FluxFlow.Components.RequestReply` remains a direct-code coordinator package
  with self-validating request/reply and tracker option contracts, and is
  intentionally not covered by composition adapters in this pass. Its
  coordinator reports invalid null request contexts and response messages as
  diagnostics without stopping later valid messages, and emits `Received`,
  `Published`, and terminal/diagnostic events around correlated request
  publication and reply handling.
- `FluxFlow.Components.Storage` provides storage nodes and host-owned store
  contracts, including normalized `StorageStoreContext` values for backend
  factories plus normalized request, record, and result text for config-bound
  callers. Storage node options normalize default collections and fail fast for
  invalid capacity, query paging, and write mode values.
- `FluxFlow.Components.Designer` provides engine/composition-neutral design
  metadata contracts, catalogs, and package-owned provider interfaces.
- `FluxFlow.Components.Storage.FileSystem` and
  `FluxFlow.Components.Storage.SqlFile` provide concrete `IStorageStore`
  backends, backend factories, direct keyed store registration helpers, and
  keyed factory registration helpers consumed by host-owned storage
  registration. Those helpers reject invalid service/key/options arguments and
  null options factory results before creating keyed stores or factories. The
  backends also reject unsupported storage write modes and use deterministic
  per-query expiration timestamps.

Composition hosts consume these packages indirectly through adapter-owned
resources or host setup. They should not add `FluxFlow.Composition` node
factories unless a package later exposes actual standalone node behavior.

## Engine

`FluxFlow.Engine` exposes a small set of public namespaces. The goal for v1 is
that a host can author nodes, load executable definitions, build a runtime, and
observe lifecycle state without depending on internal runtime details.

## Canonical Stable Ports

Namespace:

```text
FluxFlow.Engine.Ports
```

Main types:

- `ApplicationPortRuntimeBuilder`
- `ApplicationPortRuntime`
- `ApplicationPortRevisionBuilder`
- `ApplicationPortRevision`
- `ApplicationPortRevisionLease`
- `ApplicationPortRevisionInfo`
- `ApplicationPortMetadata`
- `PortSendResult`
- `PortReceiveResult<T>`
- `PortObserveResult<T>`
- `PortObservation<T>`
- `PortRequestResult<T>`
- `ApplicationPortRejection`

This additive vNext surface uses
`FluxFlow.Composition.Addressing.ApplicationAddress` and
`FluxFlow.Nodes.FlowMessage<T>`. Stable bounded input mailboxes and output
broadcast hubs remain addressable while component targets and sources are
replaced. Direct receive and observation are broadcast subscribers, not
competing consumers. `Connect(...)` activates a statically compiled canonical
link with isolated condition and target failures. Expected full, unavailable,
completed, and timeout states are result values; caller cancellation remains a
canceled operation.

The bounded `Rejections` stream records port-local delivery failures. Canonical
system events and diagnostics are described below. A prepared revision stages
replacement sources, pauses only affected dispatchers, replaces input targets,
and swaps a complete immutable compiled-link snapshot. Stable addresses and
exact payload types must already be registered; queued payload migration and
dynamic runtime port creation remain deferred.

## Canonical Runtime Signals

Namespace:

```text
FluxFlow.Engine.Signals
```

Main types:

- `ApplicationSystemEvent`
- `ApplicationSystemEventCategory`
- `ApplicationSystemEventNames`
- `SystemEventPublishResult`
- `ApplicationDiagnostic`
- `ApplicationDiagnosticKind`
- `ApplicationDiagnosticLevel`
- `ApplicationDiagnosticNames`
- `ApplicationRuntimeInstrumentation`

`ApplicationPortRuntimeBuilder` automatically registers
`System.Events.Output` as `ApplicationSystemEvent` and
`System.Diagnostics.Output` as `ApplicationDiagnostic`.
`ApplicationPortRuntimeBuilder.SystemOutputs` supplies their exact metadata to
the canonical link compiler.

`PublishSystemEventAsync` applies bounded asynchronous backpressure and keeps
accepted events ordered. `TryPublishDiagnostic` is bounded best effort and
returns `false` immediately on overflow. Both streams use `FlowMessage<T>` as
the only trace, correlation, message, and causation authority. Events and
diagnostics therefore remain normal workflow data without duplicating envelope
identity in their payloads.
`ApplicationPortRuntime` implements the shared revision-event sink and maps
revision phases into the same reliable system stream.

`ApplicationRuntimeStatus` and `ApplicationPortStatus` are snapshots exposed by
the stable-port runtime; they are not a universal State port. Runtime failures,
port activity, and direct request timing are mapped into system events or
diagnostics. Accepted diagnostics integrate with standard `ILogger`,
`ActivitySource`, `Meter`, and `DiagnosticSource` providers, with host-provider
exceptions isolated from runtime processing.

## Hosting

Namespace:

```text
FluxFlow.Engine
```

Main types:

- `FlowApplicationHost`
- `FlowApplicationHostState`
- `FlowApplicationHostBuildResult`
- `FlowApplicationHostBuildError`
- `FlowApplicationConfigurationLoader`
- `FlowApplicationConfigurationException`

Use `FlowApplicationHost` when the host wants one object to own build, start,
stop, runtime diagnostics, and disposal.

Applications that use link `when` conditions must pass an `IFlowExpressionEngine`
to `FlowApplicationHost.Create(...)`.

## Definitions

Namespace:

```text
FluxFlow.Engine.Definitions
```

Main types:

- `ApplicationDefinition`
- `WorkflowDefinition`
- `NodeDefinition`
- `LinkDefinition`
- `ApplicationDefinitionJson`
- `ApplicationDefinitionValidator`
- `ApplicationDefinitionValidationResult`
- `ApplicationDefinitionValidationError`
- `ApplicationDefinitionValidationErrorCode`
- `NodeType`
- `NodeName`
- `WorkflowName`
- `PortName`
- `NodeAddress`
- `PortAddress`
- `WellKnownScopes`

Definitions are DTO-style contracts. Their dictionaries are intentionally
mutable for JSON loading and code-based authoring. Hosts can keep richer
workspace files, then project only executable resources and workflows into
`ApplicationDefinition`.

## Runtime

Namespace:

```text
FluxFlow.Engine.Runtime
```

Main types:

- `ApplicationRuntimeBuilder`
- `ApplicationRuntime`
- `Workflow`
- `RuntimeNode`
- `RuntimeNodeFactoryRegistry`
- `RuntimeNodeFactoryContext`
- `RuntimeNodeBuilder`
- `InputPort<T>`
- `OutputPort<T>`
- `ApplicationRuntimeBuildResult`
- `ApplicationRuntimeBuildError`
- `ApplicationRuntimeBuildErrorCode`
- `ApplicationRuntimeNodeStartException`
- `ApplicationState`
- `ApplicationStateChanged`
- `WorkflowState`
- `WorkflowStateChanged`
- `RuntimeFlowDiagnostic`
- `IFlowNodeRegistration`
- `FlowNodeRegistration`
- `IFlowNodeModule`
- `FlowNodeModule`

Use `ApplicationRuntimeBuilder` when the host wants to build the runtime
directly. Register every node factory explicitly through
`RuntimeNodeFactoryRegistry`.

Runtime build catches missing node types, missing ports, type mismatches,
unsupported conditional links, and missing expression engines before startup.

## Node Authoring

Namespace:

```text
FluxFlow.Engine.Components
```

Main types:

- `IFlowNode`
- `FlowNodeBase`
- `SourceFlowNode<TOutput>`
- `SinkFlowNode<TInput>`
- `TransformFlowNode<TInput,TOutput>`
- `MapFlowNode<TInput,TOutput>`
- `EventFlowNodeBase`
- `FlowNodeId`
- `FlowError`
- `FlowErrorCodes`
- `FlowEvent`
- `FlowDiagnostic`
- `FlowDiagnosticLevel`
- `IFlowDiagnosticSource`
- `IFlowEventSource`

Use these types for custom host nodes and reusable component package nodes.
Prefer the base classes when the node fits source, sink, transform, map, event,
error, or diagnostic patterns.

## Expression And Mapping Contracts

Namespace:

```text
FluxFlow.Mapping
```

Main types:

- `IFlowExpressionEngine`
- `IFlowCompiledExpression<T>`
- `FlowMapContext`
- `IFlowMapContextFactory<TInput>`
- `IFlowPredicate<TInput>`
- `ExpressionFlowPredicate<TInput>`
- `DelegateFlowPredicate<TInput>`
- `IFlowMapper<TInput,TOutput>`
- `ExpressionFlowMapper<TInput,TOutput>`
- `DelegateFlowMapper<TInput,TOutput>`

These contracts live in an engine-free leaf package. The engine and standalone
component packages consume them, but concrete expression languages, expression
validation, and context factory registration remain host-owned. `FlowMapContext`
copies assigned variable dictionaries with ordinal key comparison so each
per-message expression context is stable after creation. Expression mapper and
predicate adapters compile during construction and fail fast when a host engine
returns an invalid null compiled expression.

## Stability Notes

For v1, the stable engine surface is the public API in these namespaces plus the
JSON shape documented in the definitions guide. Internal runtime helpers,
collectors, fanout queues, and cleanup helpers are not public extension points.

Next: [Engine Compatibility](15-engine-compatibility.md)
