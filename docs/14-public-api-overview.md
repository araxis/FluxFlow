# Public API Overview

FluxFlow's default public surface is standalone-node-first:

- `FluxFlow.Nodes` for node authoring.
- `FluxFlow.Composition` for fluent/config composition of standalone nodes.
- `FluxFlow.Engine` for the optional advanced engine runtime.

## Node Kit

Namespace:

```text
FluxFlow.Nodes
```

Main types:

- `FlowMessage<T>`
- `CorrelationId`
- `FlowNode<TInput,TOutput>`
- `FlowNodeOptions`
- `FlowSource<TOutput>`
- `FlowSourceOptions`
- `IFlowNode`
- `IFlowSource`
- `FlowError`
- `FlowEvent`
- `FlowEventLevel`

Use these types to author standalone nodes directly. `FlowNodeOptions`
configures bounded transform intake and validates non-positive capacities and
parallelism values when assigned. `FlowSourceOptions` lets source nodes opt into
bounded broadcast output and awaitable output-block acceptance while sources
that do not pass options keep the original unbounded broadcast behavior. It
allows `UnboundedOutputCapacity` and validates other output capacities when
assigned. `FlowMessage<T>` headers and `FlowEvent` attributes copy assigned
dictionaries with ordinal key comparison, keeping broadcast envelopes and
diagnostics stable after creation.

## Composition

Namespace:

```text
FluxFlow.Composition
```

Main types:

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

Use these types when the host wants direct standalone-node composition from
fluent C# or `IConfiguration` JSON without depending on the engine. Definition
DTO collection properties copy assigned dictionaries and lists with ordinal key
comparison so caller-owned collections cannot mutate a built definition.
Node and port references trim assigned segments and reject empty dotted segments
when parsed from fluent or configuration link strings.
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

- `CompositionHostingOptions`
- `CompositionHostingBuilder`
- `ICompositionRuntimeHost`
- `CompositionRuntimeHost`
- `CompositionHostingException`
- `StaticCompositionDefinitionSource`
- `ConfigurationCompositionDefinitionSource`
- `ICompositionNodeRegistryContributor`
- `CompositionNodeFactoryContextResourceExtensions`

Use these types when a .NET host wants DI to load, build, start, stop, and
observe a composition runtime. Resource helpers resolve named node resource
references from keyed DI services; adapter packages still own the resources.
Resource helper slot names and configured keyed service references are trimmed
before lookup so configuration whitespace does not change resource identity.
Hosted and manual lifecycle calls are idempotent at this boundary, so repeated
start or stop requests do not start or complete the same runtime more than
once. A stopped runtime is not restarted by the host.

## HTTP Composition

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
`http.client` node factory. The factory resolves a keyed `HttpClient` resource;
the host still owns client lifetime and transport policy. Invalid numeric
`HttpClientNodeOptions` values fail during build as factory diagnostics when the
host is configured to collect build failures.

`HttpComponentDesignMetadataProvider` exposes neutral Designer metadata for the
HTTP client composition node, including existing options, fixed ports, and
resource hints for the required `client` resource and optional `clock`
resource. `HttpClient` instances and clocks remain host-owned keyed resources.

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

Use `RegisterMapper<TInput,TOutput>()` from the optional
`FluxFlow.Components.Mapping.Composition` package when a composition host wants
closed generic `flow.mapper` node factories. The factory resolves a keyed
`IFlowExpressionEngine` resource; optional keyed context factory and clock
resources stay host-owned. Invalid mapper options fail during build as factory
diagnostics when the host is configured to collect build failures.

`MappingComponentDesignMetadataProvider` exposes neutral Designer metadata for
the `flow.mapper` composition node so hosts can compose palette, editor,
validation, or documentation hints without copying package descriptors. The
metadata includes editable options, ports, and resource hints for the required
`engine` resource plus optional `contextFactory` and `clock` resources.

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

Use `RegisterAssertion<TInput>()` from the optional
`FluxFlow.Components.Assertions.Composition` package when a composition host
wants closed generic `flow.assert` node factories. The factory resolves a keyed
`IFlowExpressionEngine` resource; optional keyed typed context factory and clock
resources stay host-owned.

`AssertionsComponentDesignMetadataProvider` exposes neutral Designer metadata
for the `flow.assert` composition node so hosts can compose palette, editor,
validation, or documentation hints without copying package descriptors. The
metadata includes editable options, ports, and resource hints for the required
`engine` resource plus optional `contextFactory` and `clock` resources.

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

Use `RegisterFilter<TInput>()` and `RegisterWhen<TInput>()` from the optional
`FluxFlow.Components.Control.Composition` package when a composition host wants
closed generic `flow.filter` and `flow.when` node factories. The factories
resolve a keyed `IFlowExpressionEngine` resource; optional keyed typed context
factory and clock resources stay host-owned. Invalid control options fail
during build as factory diagnostics when the host is configured to collect
build failures.

`ControlComponentDesignMetadataProvider` exposes neutral Designer metadata for
the `flow.filter` and `flow.when` composition nodes so hosts can compose palette,
editor, validation, or documentation hints without copying package descriptors.
The metadata includes editable options, ports, and resource hints for the
required `engine` resource plus optional `contextFactory` and `clock` resources.

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

Use `RegisterJsonSchemaValidator<TInput>()` from the optional
`FluxFlow.Components.Validation.Composition` package when a composition host
wants closed generic `json.schema-validator` node factories. The factory binds
`JsonSchemaValidatorOptions`, compiles inline `schema` or `schemaPath` during
composition build, and resolves optional keyed typed selector and clock
resources through the host.
Invalid validator options fail during build as factory diagnostics when the host
is configured to collect build failures.

`ValidationComponentDesignMetadataProvider` exposes neutral Designer metadata
for the `json.schema-validator` composition node so hosts can compose palette,
editor, validation, or documentation hints without copying package descriptors.
The metadata includes editable options, fixed ports, and resource hints for the
optional `selector` and `clock` resources.

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
- `SourcesCompositionNodeTypes`
- `SourcesCompositionPortNames`
- `SourcesCompositionResourceNames`

Use `RegisterGeneratedSource<TOutput>()` and `RegisterSequenceSource()` from the
optional `FluxFlow.Components.Sources.Composition` package when a composition
host wants generated or sequence source factories. The generated factory
deserializes inline `items` into the closed output type; both factories resolve
optional keyed `TimeProvider` resources through the host.
Invalid source option values fail during composition build through the factory
path, so hosts that collect build diagnostics receive `FactoryFailed` entries
instead of a partially created runtime.

`SourcesComponentDesignMetadataProvider` exposes neutral Designer metadata for
generated and sequence source composition nodes so hosts can compose palette,
editor, validation, or documentation hints without copying package descriptors.
The metadata includes inline generated `items` as JSON node configuration,
fixed output ports, and a resource hint for the optional `clock` resource.

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

Use `RegisterCounter<TInput>()`, `RegisterLogger<TInput>()`, and
`RegisterMetrics<TInput>()` from the optional
`FluxFlow.Components.Observability.Composition` package when a composition host
wants counter, logger, or metrics node factories. The factories bind existing
observability options and resolve host-owned keyed expression, selector,
context, and clock resources. Invalid observability options fail during build
as factory diagnostics when the host is configured to collect build failures.

`ObservabilityComponentDesignMetadataProvider` exposes neutral Designer metadata
for the three observability composition nodes, including existing option records
fixed ports, and host-owned resource hints. Counter metadata includes the
conditionally required expression engine plus optional context factory and
clock resources. Logger metadata includes the dynamic `attribute:{name}`
selector resource pattern, and metrics metadata includes the optional
`sizeSelector` and `clock` resources. Expression engines, context factories,
selectors, and clocks remain host-owned keyed resources.

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
a `metrics.aggregate` node factory. The factory binds existing
`MetricsAggregateOptions` and can resolve an optional keyed `TimeProvider`
resource through the host.

`MetricsComponentDesignMetadataProvider` exposes neutral Designer metadata for
the `metrics.aggregate` composition node, including existing metrics aggregate
options, fixed ports, and a resource hint for the optional `clock` resource.

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

Use `RegisterSwitch<TInput>()`, `RegisterFork<TInput>()`,
`RegisterMerge<TInput>()`, `RegisterWindow<TInput>()`,
`RegisterCorrelation<TInput>()`, and `RegisterJoin<TLeft,TRight>()` from the
optional `FluxFlow.Components.Routing.Composition` package when a composition
host wants routing node factories. Switch, correlation, and join factories
resolve host-owned keyed selector delegates; all factories can resolve an
optional keyed `TimeProvider` resource through the host.
Invalid routing options fail during build as factory diagnostics when the host
is configured to collect build failures.

`RoutingComponentDesignMetadataProvider` exposes neutral Designer metadata for
the six routing composition nodes so hosts can compose palette, editor,
validation, or documentation hints without copying package descriptors. The
metadata describes built-in ports and option-defined dynamic output surfaces
plus host-owned resource hints for selector delegates and `clock`.

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
wants serialization and encoding factories. The factories bind existing
serialization options and can resolve an optional keyed `TimeProvider` resource
through the host.

`SerializationComponentDesignMetadataProvider` exposes neutral Designer
metadata for the six serialization composition nodes so hosts can compose
palette, editor, validation, or documentation hints without copying package
descriptors. The metadata includes shared options, fixed ports, and a resource
hint for the optional `clock` resource.

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
a `payload.inspect` node factory. The factory binds existing
`PayloadInspectOptions` and can resolve an optional keyed `TimeProvider`
resource through the host.

`PayloadsComponentDesignMetadataProvider` exposes neutral Designer metadata for
the `payload.inspect` composition node so hosts can compose palette, editor,
validation, or documentation hints without copying package descriptors. The
metadata includes options, fixed ports, and a resource hint for the optional
`clock` resource.

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
hint for the optional `clock` resource.

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
`state.reducer` node factory. The factory binds existing `StateReducerOptions`,
resolves a required keyed `IFlowExpressionEngine`, and can resolve an optional
keyed `TimeProvider` resource through the host.

`StateComponentDesignMetadataProvider` exposes neutral Designer metadata for
`state.reducer`, including the existing reducer options, fixed ports, and
resource hints for the required `engine` resource plus optional `clock`
resource. The `engine` option is diagnostic/config metadata, not DI selection.

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
storage node factories. The factories bind existing storage options, resolve a
required keyed `IStorageStore` or `IStorageStoreFactory`, and can resolve an
optional keyed `TimeProvider` resource through the host. Factory resources are
opened during composition build and released with composed node disposal; direct
stores remain host-owned.

`StorageComponentDesignMetadataProvider` exposes neutral Designer metadata for
the four storage composition nodes, including existing storage options and fixed
ports, plus resource hints for the required `store` resource and optional
`clock` resource. The `store` resource may point at either a keyed
`IStorageStore` or keyed `IStorageStoreFactory`.

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

- `ISessionStoreFactory`
- `SessionStoreContext`
- `SessionStoreLease`
- `SessionComponentOptions`

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

`SessionsComponentDesignMetadataProvider` exposes neutral Designer metadata for
the three session composition nodes, including existing session options and fixed
ports, plus resource hints for the required `store` resource and optional
`clock` resource. The `store` resource may point at either a keyed
`ISessionStore` or keyed `ISessionStoreFactory`; the `store` option is
diagnostic/config metadata, not DI selection.

The base Sessions package owns the neutral store factory, context, lease, and
component option helpers used by direct hosts and composition adapters; it still
does not own any concrete persistence backend.

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
wants an `event.projection` node factory. The factory binds existing
`EventProjectionOptions` and can resolve an optional keyed `TimeProvider`
resource through the host.

`ProjectionsComponentDesignMetadataProvider` exposes neutral Designer metadata
for the `event.projection` composition node, including existing projection
options, fixed ports, and a resource hint for the optional `clock` resource.
The final snapshot lifecycle remains a direct node API in this composition
pass.

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
wants an `event.expectation` node factory. The factory binds existing
`EventExpectationOptions` and can resolve an optional keyed `TimeProvider`
resource through the host.

`ExpectationsComponentDesignMetadataProvider` exposes neutral Designer metadata
for the `event.expectation` composition node, including existing expectation
options, fixed ports, and a resource hint for the optional `clock` resource.
Completion result flushing remains a direct node API in this composition pass.

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
`mqtt.publish` and `mqtt.trigger` node factories. The factories resolve keyed
`IMqttPublisher` and `IMqttTriggerSource` resources; concrete MQTT adapters or
the host still own broker/client registration.

`MqttComponentDesignMetadataProvider` exposes neutral Designer metadata for the
MQTT publish and trigger composition nodes, including existing options, fixed
ports, and resource hints for `publisher`, `triggerSource`, and optional
`clock` resources. Publisher, trigger source, and clock resources remain
host-owned.

## Designer Metadata

Namespace:

```text
FluxFlow.Components.Designer
FluxFlow.Components.Designer.Contracts
```

Main types:

- `ComponentType`
- `ComponentPortName`
- `ComponentDesignMetadata`
- `OptionDesignMetadata`
- `OptionChoiceMetadata`
- `OptionValueKind`
- `ResourceDesignMetadata`
- `PortDesignMetadata`
- `PortDirection`
- `IComponentDesignMetadataProvider`
- `ComponentDesignMetadataCatalog`
- `ComponentDesignMetadataModule`
- `ComponentDesignMetadataValidator`
- `DesignerMetadataValidationError`

Use these types when reusable packages want to describe neutral palette,
editor, validation, and generated-doc metadata without depending on either the
composition runtime or the engine runtime.

`ComponentDesignMetadataValidator` enforces identifier, option, choice,
resource, port, and attribute consistency. Enum options must define choices,
choice lists are valid only on enum options, option defaults must match their
declared kind, and min/max constraints are limited to number and duration
options.
`ComponentDesignMetadataCatalog` validates and snapshots registered metadata so
caller-owned option, resource, port, choice, and attribute collections cannot
mutate catalog contents after registration.

## Support Packages

These packages are intentionally not standalone node composition adapters:

- `FluxFlow.Components.Configuration` validates resource and secret references,
  including trimmed resource option paths and resource option metadata
  diagnostics with normalized valid metadata maps, request collection
  snapshotting, and a fluent `ConfigurationValidationRequestBuilder` over the
  same validation DTOs.
- `FluxFlow.Components.Resources` defines named resource contracts and lookup
  diagnostics, including trimmed resource names, kinds, display text, and
  null-safe normalized metadata and attribute validation plus defensive
  diagnostic metadata copying.
- `FluxFlow.Components.Secrets` defines secret references, resolution results,
  option helpers, redaction helpers, trimmed secret names, versions, kinds,
  option paths, null-safe normalized metadata validation, and defensive
  diagnostic metadata copying.
- `FluxFlow.Components.Expressions` provides expression engine and context
  factory registries used by adapters that resolve host-owned expression
  services, including deterministic most-specific context factory lookup.
- `FluxFlow.Components.Journal` provides runtime-neutral journal event input,
  record mapping, store contracts, store factory/context/lease helpers,
  retention option validation, and named in-memory store factory support for
  hosts.
- `FluxFlow.Components.RequestReply` remains a direct-code coordinator package
  with self-validating request/reply and tracker option contracts, and is
  intentionally not covered by composition adapters in this pass.
- `FluxFlow.Components.Storage` provides storage nodes and host-owned store
  contracts, including normalized `StorageStoreContext` values for backend
  factories plus normalized request, record, and result text for config-bound
  callers. Storage node options normalize default collections and fail fast for
  invalid capacity, query paging, and write mode values.
- `FluxFlow.Components.Designer` provides engine/composition-neutral design
  metadata contracts, catalogs, and package-owned provider interfaces.
- `FluxFlow.Components.Storage.FileSystem` and
  `FluxFlow.Components.Storage.SqlFile` provide concrete `IStorageStore`
  backend factories plus keyed factory registration helpers consumed by
  host-owned storage registration, including direct rejection of unsupported
  storage write modes and deterministic per-query expiration timestamps.

Composition hosts consume these packages indirectly through adapter-owned
resources or host setup. They should not add `FluxFlow.Composition` node
factories unless a package later exposes actual standalone node behavior.

## Engine

`FluxFlow.Engine` exposes a small set of public namespaces. The goal for v1 is
that a host can author nodes, load executable definitions, build a runtime, and
observe lifecycle state without depending on internal runtime details.

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
