# Public API Overview

FluxFlow is organized as independently versioned packages. Runtime component
packages expose standalone Dataflow nodes. Composition packages add canonical
JSON registration and Designer metadata. Hosts own concrete clients, stores,
clocks, expression engines, credentials, and other resources.

## Foundation

### FluxFlow.Data 2.x

- `FlowContent`: exact owned bytes plus optional content type and encoding.
- `FlowError`: transport-neutral processing failure with stable code, category,
  transient flag, and detached optional JSON details.

The package no longer defines a universal value tree, result wrapper, or codec
catalog. See [Flow Data Contracts](20-flow-data-contracts.md).

### FluxFlow.Nodes 3.x

- `FlowMessage<T>`: one active value or `FlowError` plus trace, message,
  causation, optional correlation, timestamp, and immutable string headers.
- `FlowNode<TInput,TOutput>` and `FlowSource<TOutput>`: standalone Dataflow
  foundations with normal Output, Events, Completion, and async disposal.
- `IFlowNode`, `IFlowSource`, `FlowEvent`, and typed identifier contracts.

Errors travel on normal outputs. `Events` is diagnostics; `Completion` reports
lifecycle completion or an unrecoverable block fault.

### FluxFlow.Mapping 1.x

Defines `IFlowExpressionEngine`, compiled expressions, typed mapper/predicate
interfaces, delegate/expression implementations, and `FlowMapContext`. It is an
engine-free abstraction and does not own a dynamic workflow representation.

### FluxFlow.Coordination 2.x and FluxFlow.Resilience 1.x

Coordination supplies bounded generic pending exchanges with deterministic
timeouts and exact-once settlement. Resilience supplies transport-neutral retry
policies, schedules, budgets, jitter, and state transitions. Components keep
their own protocol classification and lifecycle ownership.

## Application Runtime

### FluxFlow.Composition 4.x

Owns the canonical application model (`Resources` and `Workflows`), component
definitions, addresses, aliases, validation, explicit registration, link
compilation, processing profiles, code-first runtime ownership, and component
event fan-in. Links may be declared at either endpoint and compile into one
canonical model. Signal feedback is explicit; ordinary data cycles remain
invalid.

### FluxFlow.Composition.Hosting 4.x

Owns definition sources, immutable service-provider snapshots, hosted
lifecycle, and transactional revision activation. A candidate revision is
prepared in isolation and becomes active atomically; failed preparation leaves
the prior revision running.

### FluxFlow.Engine 4.x

Is the optional canonical runtime assembler. It resolves resources, activates
components, binds links, publishes stable direct ports, emits system events and
diagnostics, and coordinates revision rollback. Component packages do not
depend on Engine.

### FluxFlow.Fluent 2.x

Builds typed code-first graphs over the same node and composition contracts.
Fluent observation receives normal value-or-error output messages rather than a
parallel universal error stream.

## Component Contracts

The following table lists the maintained runtime surface. Every output type is
inside `FlowMessage<T>` and may instead carry `FlowError`.

| Family | Primary nodes | Value contracts |
|--------|---------------|-----------------|
| Mapping | `FlowMapperNode<TInput,TOutput>`, `JsonMapperNode` | typed T input/output; explicit JSON specialization |
| Assertions | `AssertionNode<T>`, `JsonAssertionNode` | `T` -> `AssertionResult<T>` |
| Validation | `JsonSchemaValidatorNode` | `JsonElement` -> `JsonSchemaValidationResult` |
| Routing | `WindowNode<T>`, `CorrelationNode<T>`, `JoinNode<TLeft,TRight>` plus JSON specializations | typed windows and correlation/join outcomes |
| State | `StateReducerNode<T>`, `JsonStateReducerNode` | `StateReducerInput<T>` -> `StateReducerResult<T>` |
| Sources | `GeneratedSourceNode<T>`, `SequenceSourceNode` | `T` or `SequenceItem` |
| Timers | interval/schedule sources and generic delay/throttle/debounce nodes | typed ticks or pass-through T |
| FileSystem | read/write transforms and directory/watch sources | exact content plus `DirectoryEntry`/`FileChange` |
| Serialization | JSON, text, and Base64 conversion nodes | explicit `FlowContent`, `JsonElement`, and string conversions |
| Payloads | `PayloadInspectNode` | `FlowContent` -> `PayloadInspectionResult` |
| Observability | generic counter/logger/metrics nodes plus JSON specializations | typed input -> snapshot/log records |
| Metrics | `MetricsAggregateNode` | `MetricSampleInput` -> `MetricSnapshotOutput` |
| Projections | `EventProjectionNode` | `ProjectionEvent` -> `EventProjectionSnapshot` |
| Expectations | `EventExpectationNode` | `ProjectionEvent` -> `EventExpectationResult` |
| HTTP | `HttpClientNode` | `HttpClientRequest` -> `HttpResponseResult` |
| Storage | put/get/delete/query nodes | typed request and outcome contracts over host stores |
| Sessions | recorder/replay/query nodes | typed content records and query outcomes |
| Resilience | `FlowRetryNode<T>` | T attempts, signal inputs, and typed retry outcomes |
| MQTT | control, publish, receive, and events nodes | typed client requests/results and exact received content |

Expected business variants remain in these result contracts. Processing
failures set `FlowMessage.IsError`; they are not wrapped in another result type.

## Mapping, JSON, and Expressions

Typed components keep CLR values typed. Configuration-driven mapping,
assertion, routing, state, and validation registrations use explicit
`JsonElement` specializations where their document contract is schema-less.
Expression engines adapt those values according to their own language. A mapper
may intentionally emit a CLR record, dictionary, or `ExpandoObject`, but no
dynamic object is required by the runtime.

## Content and Transport Boundaries

`FlowContent` preserves bytes exactly. HTTP, MQTT, FileSystem, Storage, and
Sessions use it where a raw body is the real contract. Serialization nodes make
text, JSON, and Base64 conversion visible. There is no lazy decode cache or
hidden codec lookup. Decode before broadcast to share one immutable decoded
value, or branch first to retain both raw and decoded paths.

MQTT's core controller owns logical client behavior while concrete adapter
packages own provider sessions. Broker resources own endpoint defaults; client
resources own identity, credentials, reconnect, subscriptions, and lifecycle.
Workflow acknowledgement remains separate from broker acknowledgement.

HTTP's client node uses a host-owned `HttpClient`; the ASP.NET Core adapter owns
endpoint integration and request/reply wiring. Neither moves server or client
ownership into Engine.

## Composition Adapters and Designer Metadata

Each maintained `.Composition` package registers stable component type names,
fixed typed ports, flat options, host-owned resource references, and Designer
option/resource hints. The normal configuration shape is:

```json
{
  "Resources": {
    "Expressions": {
      "Default": { "Type": "expression.engine" }
    }
  },
  "Workflows": {
    "Orders": {
      "Map": {
        "Type": "data.map",
        "Expression": "...",
        "Engine": "Resources.Expressions.Default",
        "Input": "Receive.Output",
        "Output": "Validate.Input"
      }
    }
  }
}
```

There are no maintained `Composition`, `Nodes`, or root `Links` wrappers.
Component and workflow names come from object keys. Resources may be nested and
use exact addresses such as `Resources.Expressions.Default`.

## Resource and Ownership Packages

`FluxFlow.Components.Resources`, Secrets, and Configuration provide explicit
resource addressing, secret resolution, and configuration validation.
FileSystem and SQL-file Storage adapters implement the neutral store boundary.
They do not change workflow message semantics or own host lifetime.

## Error and Diagnostic Policy

- Normal per-message failures are `FlowError` data on Output.
- Domain-negative results stay in the declared result type when they are valid
  operation outcomes.
- Components propagate incoming errors unless explicitly error-aware.
- Events describe input, output, lifecycle, and operational diagnostics.
- Block faults are reserved for violated invariants or unrecoverable lifecycle
  failures and do not define application host lifetime.
- Links and expressions may route on `isError`, `error.code`, typed result
  properties, headers, and other message fields.

## Public API and Versioning

Package projects target supported stable frameworks and avoid preview union
syntax. The value-or-error invariant is implemented behind private construction
and can adopt a future stable language union without changing its meaning.

The public API baseline in `eng/public-api/baseline.txt` records normalized
source declarations. SDK
package validation remains the binary compatibility gate. Major versions in
this release train intentionally remove the former universal data contracts;
no compatibility aliases recreate them.


## Shipped Package Index

The manifest is authoritative for shipped package identities and project-owned versions.

| Package | Version | Composition API or role |
|---------|---------|-------------------------|
| `FluxFlow.Data` | `2.0.0` | runtime or support package |
| `FluxFlow.Nodes` | `3.0.0` | runtime or support package |
| `FluxFlow.Coordination` | `2.0.0` | runtime or support package |
| `FluxFlow.Resilience` | `1.0.0` | runtime or support package |
| `FluxFlow.Components.Resilience` | `2.0.0` | runtime or support package |
| `FluxFlow.Components.Resilience.Composition` | `2.0.0` | `RegisterFlowRetry`; `ResilienceComponentDesignMetadataProvider` |
| `FluxFlow.Composition` | `4.0.0` | composition migration/support package |
| `FluxFlow.Composition.Hosting` | `4.0.0` | runtime or support package |
| `FluxFlow.Mapping` | `1.0.3` | runtime or support package |
| `FluxFlow.Components.RequestReply` | `2.0.0` | runtime or support package |
| `FluxFlow.Components.Http.AspNetCore` | `2.0.0` | runtime or support package |
| `FluxFlow.Engine` | `4.0.0` | runtime or support package |
| `FluxFlow.Components.Expressions` | `2.1.3` | runtime or support package |
| `FluxFlow.Components.Mqtt` | `7.0.0` | runtime or support package |
| `FluxFlow.Components.Mqtt.Composition` | `4.0.0` | `RegisterMqttNodes`; `MqttComponentDesignMetadataProvider` |
| `FluxFlow.Components.Mqtt.MqttNet` | `3.0.0` | runtime or support package |
| `FluxFlow.Components.Mqtt.PulseMqtt` | `4.0.0` | runtime or support package |
| `FluxFlow.Components.Mapping` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Mapping.Composition` | `4.0.0` | `RegisterMapper`; `MappingComponentDesignMetadataProvider` |
| `FluxFlow.Components.Control` | `5.0.0` | runtime or support package |
| `FluxFlow.Components.Control.Composition` | `3.0.0` | composition migration/support package |
| `FluxFlow.Components.Assertions` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Assertions.Composition` | `4.0.0` | `RegisterAssertion`; `AssertionsComponentDesignMetadataProvider` |
| `FluxFlow.Components.Sources` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Sources.Composition` | `4.0.0` | `RegisterGeneratedSource`, `RegisterSequenceSource`; `SourcesComponentDesignMetadataProvider` |
| `FluxFlow.Components.Routing` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Routing.Composition` | `4.0.0` | `RegisterCorrelation`, `RegisterJoin`, `RegisterWindow`; `RoutingComponentDesignMetadataProvider` |
| `FluxFlow.Components.Validation` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Validation.Composition` | `4.0.0` | `RegisterJsonSchemaValidator`; `ValidationComponentDesignMetadataProvider` |
| `FluxFlow.Components.FileSystem` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.FileSystem.Composition` | `4.0.0` | `RegisterDirectoryEnumerate`, `RegisterFileRead`, `RegisterFileWatch`, `RegisterFileWrite`; `FileSystemComponentDesignMetadataProvider` |
| `FluxFlow.Components.Observability` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Observability.Composition` | `4.0.0` | `RegisterCounter`, `RegisterLogger`, `RegisterMetrics`; `ObservabilityComponentDesignMetadataProvider` |
| `FluxFlow.Components.Timers` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Timers.Composition` | `4.0.0` | `RegisterTimerDebounce`, `RegisterTimerDelay`, `RegisterTimerInterval`, `RegisterTimerSchedule`, `RegisterTimerThrottle`; `TimersComponentDesignMetadataProvider` |
| `FluxFlow.Components.Payloads` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Payloads.Composition` | `3.0.0` | `RegisterPayloadInspect`; `PayloadsComponentDesignMetadataProvider` |
| `FluxFlow.Components.Http` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Http.Composition` | `4.0.0` | `RegisterHttpNodes`; `HttpComponentDesignMetadataProvider` |
| `FluxFlow.Components.Serialization` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Serialization.Composition` | `3.0.0` | `RegisterBase64Decode`, `RegisterBase64Encode`, `RegisterJsonParse`, `RegisterJsonStringify`, `RegisterTextDecode`, `RegisterTextEncode`; `SerializationComponentDesignMetadataProvider` |
| `FluxFlow.Components.Metrics` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Metrics.Composition` | `3.0.0` | `RegisterMetricsAggregate`; `MetricsComponentDesignMetadataProvider` |
| `FluxFlow.Components.Projections` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Projections.Composition` | `3.0.0` | `RegisterEventProjection`; `ProjectionsComponentDesignMetadataProvider` |
| `FluxFlow.Components.Expectations` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Expectations.Composition` | `4.0.0` | `RegisterEventExpectation`; `ExpectationsComponentDesignMetadataProvider` |
| `FluxFlow.Components.Designer` | `3.0.0` | runtime or support package |
| `FluxFlow.Components.Resources` | `3.0.0` | runtime or support package |
| `FluxFlow.Components.Secrets` | `3.0.0` | runtime or support package |
| `FluxFlow.Components.Configuration` | `3.0.0` | runtime or support package |
| `FluxFlow.Components.Journal` | `2.3.6` | runtime or support package |
| `FluxFlow.Components.Sessions` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Sessions.Composition` | `4.0.0` | `RegisterSessionQuery`, `RegisterSessionRecorder`, `RegisterSessionReplay`; `SessionsComponentDesignMetadataProvider` |
| `FluxFlow.Components.State` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.State.Composition` | `4.0.0` | `RegisterStateReducer`; `StateComponentDesignMetadataProvider` |
| `FluxFlow.Components.Storage` | `6.0.0` | runtime or support package |
| `FluxFlow.Components.Storage.Composition` | `4.0.0` | `RegisterStorageDelete`, `RegisterStorageGet`, `RegisterStoragePut`, `RegisterStorageQuery`; `StorageComponentDesignMetadataProvider` |
| `FluxFlow.Components.Storage.FileSystem` | `4.0.0` | runtime or support package |
| `FluxFlow.Components.Storage.SqlFile` | `4.0.0` | runtime or support package |
| `FluxFlow.Fluent` | `2.0.0` | runtime or support package |
| `FluxFlow.Fluent.Hosting` | `2.0.0` | runtime or support package |
