# Major Surface Reset Migration

This guide covers the current breaking reset of obsolete hosting, document,
alias, registry, and disconnected support-package surfaces. The runtime now has
one canonical path: register Engine directly, load canonical application
definitions, and resolve host-owned services through dependency injection.

## Runtime And Hosting

Replace forwarding hosting APIs with Engine's maintained entry points:

```csharp
services.AddFluxFlow(configuration, options =>
{
    options.InitialRevisionId = "orders-deployment-42";
    options.StartWithHost = true;
});
```

Resolve `FluxFlowApplication` for application lifecycle and stable ports. Use
the host's normal keyed-DI facilities for keyed resources and expression
services. The removed compatibility hosting package has no replacement layer.

## Definition Conversion

The runtime accepts only the canonical application document:

```json
{
  "Resources": {},
  "Workflows": {}
}
```

Documents using root `Composition`, `Nodes`, or `Links`, or Engine-specific
`Workflows` / `Nodes` wrappers, are rejected. If persisted documents still use
one of those shapes, run a one-time converter outside the runtime, validate the
result, and persist the canonical document before deployment.

Executable resource nodes remain declared under `Resources`; workflow-owned
components remain under `Workflows`. Processing declarations use the canonical
semantic processing profile and exact canonical type identity.

## Component Type Migration

Runtime and Designer lookup are exact. Replace every retired type string before
loading a definition:

| Removed type | Canonical type |
|---|---|
| `flow.mapper` | `data.map` |
| `flow.assert` | `data.assert` |
| `json.schema-validator` | `json.validate` |
| `state.reducer` | `state.reduce` |
| `event.expectation` | `event.expect` |
| `event.projection` | `event.project` |
| `metrics.aggregate` | `metric.aggregate` |
| `flow.counter` | `metric.count` |
| `flow.logger` | `log.write` |
| `flow.metrics` | `metric.measure` |
| `flow.correlation` | `flow.correlate` |
| `source.generated` | `source.items` |
| `directory.enumerate` | `directory.list` |
| `http.client` | `http.request` |
| `session.recorder` | `session.record` |
| `mqtt.control` | `mqtt.command` |
| `mqtt.trigger` | `mqtt.receive` |
| `resilience.retry` | `retry.policy` |

There is no alias normalization or Designer fallback. An obsolete value
produces an explicit unknown-type diagnostic.

For `metric.count`, rename the removed `expression` option to `predicate`.
Supplying `expression` is rejected with a targeted migration diagnostic.

## Expression Services

Remove uses of the former expression-engine and context-factory registries.
The helper-only Expressions package was removed with them. Register
`IFlowExpressionEngine` and `IFlowMapContextFactory<TInput>` directly through
the built-in host container and resolve the exact key and exact generic service
type:

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>("rules", expressionEngine);
services.AddKeyedSingleton<IFlowMapContextFactory<Order>>("rules", contextFactory);
```

There is intentionally no replacement global registry, default-engine
fallback, assignable-type search, custom resolver layer, or registration wrapper.

## Removed Support Packages

These disconnected projects and packages were removed:

| Removed package | Removed test project | Replacement |
|---|---|---|
| `FluxFlow.Composition.Hosting` | `FluxFlow.Composition.Hosting.Tests` | `FluxFlow.Engine` registration and `FluxFlowApplication` |
| `FluxFlow.Components.Expressions` | `FluxFlow.Components.Expressions.Tests` | direct built-in keyed DI using `FluxFlow.Mapping` contracts |
| `FluxFlow.Components.Resources` | `FluxFlow.Components.Resources.Tests` | host-owned keyed resources and `IApplicationResourceRegistrar` |
| `FluxFlow.Components.Secrets` | `FluxFlow.Components.Secrets.Tests` | host-owned secret integration |
| `FluxFlow.Components.Configuration` | `FluxFlow.Components.Configuration.Tests` | canonical definition validation plus host option binding |
| `FluxFlow.Components.Journal` | `FluxFlow.Components.Journal.Tests` | consumer-owned storage/adapter contracts when required |
| `FluxFlow.Data` | `FluxFlow.Data.Tests` | `FluxFlow.Nodes` 4.0.0; keep the `FluxFlow.Data` namespace and rebuild |
| `FluxFlow.Components.Control` | n/a | conditioned links, ordinary fan-out, and shared-input fan-in |
| `FluxFlow.Components.Control.Composition` | n/a | the same canonical link grammar; no replacement adapter |

The removed support packages were not part of the executable component catalog
or Engine lifecycle. Move consumer-owned contracts into the host or an explicit
adapter package. Do not create a new generic support package.

`IApplicationDefinitionSource`, `ConfigurationApplicationDefinitionSource`,
and `StaticApplicationDefinitionSource` remain public in `FluxFlow.Engine`.
They are the retained configuration and static-definition boundaries after the
hosting compatibility package removal.

## Removed Public Surface Inventory

The complete removed public-type inventory for this reset is grouped below.
Members removed from retained types follow the table.

| Former owner | Removed public types |
|---|---|
| Hosting compatibility | `ApplicationDefinitionConfigurationLoader`, `ApplicationRevisionHost`, `ApplicationRevisionHostState`, `ApplicationRevisionHostingOptions`, `ApplicationRevisionLoadResult`, `FluxFlowServiceCollectionExtensions`, `FluxFlowApplicationHostingServiceCollectionExtensions`, `FluxFlowEngineCompatibilityServiceCollectionExtensions`, `IApplicationRevisionHost` |
| Legacy migration | `LegacyCompositionDefinitionMigrator`, `LegacyEngineApplicationDefinitionMigrator` |
| Alias normalization | `ApplicationDefinitionNormalizer`, `ApplicationDefinitionNormalizationDiagnostic`, `ApplicationDefinitionNormalizationDiagnosticKind`, `ApplicationDefinitionNormalizationResult`, `ResourceTypeAliasDescriptor`, `ComponentDesignMetadataAttributeNames` |
| Expressions support | `FlowExpressionEngineRegistry`, `FlowContextFactoryRegistry<TFactory>`, `ExpressionServiceCollectionExtensions` |
| Resources support | `ResourceDescriptor`, `ResourceDiagnostic`, `ResourceDiagnosticCode`, `ResourceDiagnosticSeverity`, `ResourceKind`, `ResourceLookupResult`, `ResourceMetadataText`, `ResourceName`, `ResourceOwnership`, `ResourceReference`, `IResourceDescriptorProvider`, `IResourceLookup`, `ResourceDescriptorCatalog`, `ResourceDescriptorCatalogBuilder`, `ResourceDiagnostics`, `ResourceServiceCollectionExtensions` |
| Secrets support | `SecretDescriptor`, `SecretDiagnostic`, `SecretDiagnosticCode`, `SecretDiagnosticSeverity`, `SecretKind`, `SecretMetadataText`, `SecretName`, `SecretOptionReference`, `SecretOptionResolution`, `SecretRecord`, `SecretReference`, `SecretResolveResult`, `SecretValue`, `SecretVersion`, `ISecretDescriptorProvider`, `ISecretResolver`, `InMemorySecretResolver`, `InMemorySecretResolverBuilder`, `SecretDiagnostics`, `SecretOptionResolver`, `SecretRedactor`, `SecretServiceCollectionExtensions` |
| Configuration support | `ConfigurationValidationRequestBuilder`, `ConfigurationValidator`, `ConfigurationDiagnostic`, `ConfigurationDiagnosticSeverity`, `ConfigurationDiagnosticSource`, `ConfigurationOptionPath`, `ConfigurationResourceReference`, `ConfigurationValidationReport`, `ConfigurationValidationRequest` |
| Journal support | `IJournalStore`, `IJournalStoreFactory`, `JournalAppendResult`, `JournalEventInput`, `JournalPruneResult`, `JournalQuery`, `JournalQueryMatcher`, `JournalQueryResult`, `JournalRecord`, `JournalRecordMapper`, `JournalRetentionOptions`, `JournalStoreContext`, `JournalStoreLease`, `JournalEventInputBuilder`, `JournalStoreServiceCollectionExtensions`, `JournalComponentOptions`, `InMemoryJournalStore`, `InMemoryJournalStoreFactory` |
| Designer metadata providers | `IComponentDesignMetadataProvider`, `ComponentDesignMetadataModule`, and the 19 family `*ComponentDesignMetadataProvider` classes |

Removed members from retained types include `ComponentDescriptor.Aliases` and
its alias constructor input; `ComponentCatalog.Aliases`,
`ComponentCatalog.ResourceTypeAliases`, `TryResolveType`, and
`TryResolveResourceType`; `AddFluxFlowResourceTypeAlias`;
`ComponentDesignMetadata.Aliases`; `DesignerApplicationLoadResult.NormalizationDiagnostics`;
`ApplicationUpdateStage.Normalization`; and `FlowCounterOptions.Expression`.

The configuration-name inventory is the obsolete component/resource table
above plus counter option `expression`. Root `Composition`, `Nodes`, and
`Links`, and Engine-specific Workflows/Nodes documents are unsupported shapes.
No maintained descriptor exposed a separate port-alias facility during the
audit; port addressing remains exact and ordinal.

## Designed Component Registration Migration

All 19 active component composition families now have one authoritative
`*ComponentDefinition`. Each definition owns nested `Types`, `Options`,
`Ports`, and `Resources`. Its family service extension uses one flat
`AddComponent(...)` callback per component to author the runtime descriptor and
presentation metadata together. The descriptor remains authoritative for
structural types, message contracts, cardinality, option/resource schemas,
processing capabilities, and activation.

Remove custom provider registrations such as:

```csharp
services.AddComponentDesignMetadataProvider<MyMetadataProvider>();
```

For a maintained package, call its existing family registration. Designed
registrations add both catalogs automatically:

```csharp
services
    .AddFluxFlowComponents()
    .AddMapping()
    .AddHttp();
```

For an application-owned component, use one flat registration callback. The
callback creates the exact runtime/design pair and validates it immediately:

```csharp
services.AddFluxFlowComponents().AddComponent("orders.review", component =>
{
    component.WithDisplay(displayName: "Order Review", category: "Orders");
    component
        .UseFactory(CreateOrderReview)
        .HasInput("Input", static node => node.Input, displayName: "Input")
        .HasOutput("Output", static node => node.Output, displayName: "Output")
        .HasEvents("Events", static node => node.Events, displayName: "Events");
});
```

Use the declarative `HasInput`, `HasSignalInput`, `HasOutput`, and `HasEvents`
names. The selected node members already exist; these calls expose them through
the component contract. Port-level typed `Add...` aliases are not retained.

There is no public declaration registration, metadata factory, terminal catalog
registration, range pairing, reflection scan, provider discovery, or
metadata-only fallback.

The removed family provider classes are Assertions, Expectations, FileSystem,
HTTP, Mapping, Metrics, MQTT, Observability, Payloads, Projections, Resilience,
Routing, Serialization, Sessions, Sources, State, Storage, Timers, and
Validation `*ComponentDesignMetadataProvider`. Their former split
`*ComponentTypes`, `*ComponentOptions`, `*ComponentPorts`, and
`*ComponentResources` names move under the matching `*ComponentDefinition`.

## Adapter Package Decisions

Nineteen active component composition packages remain separate because they
prevent standalone runtime packages from acquiring Composition, Designer, and
DI dependencies. The empty Control runtime and composition migration markers
had no source, dependency, maintained consumer, or concrete active support
obligation and were retired from the source, solution, and release manifest.
Previously published versions remain restorable for migration only. No active
adapter was folded, no forwarding or replacement package was introduced, and no
aggregate component package was created.

## Data And Nodes

`FlowContent`, `FlowContentJsonConverter`, and `FlowError` now compile into the
`FluxFlow.Nodes` assembly. Their `FluxFlow.Data` namespace and source-level type
names remain unchanged, but the defining assembly identity changes. Replace the
package reference and rebuild every dependent package/application:

```xml
<PackageReference Include="FluxFlow.Nodes" Version="4.0.0" />
```

Remove the `FluxFlow.Data` package reference; do not change existing
`using FluxFlow.Data;` directives. No empty compatibility package and no type
forwarders are provided. The manifest and active release scripts no longer list
Data, and its meaningful tests now run in `FluxFlow.Nodes.Tests`.

## Link Projection And Persistence

Composition is the only owner of canonical link grammar, address resolution,
metadata/type/exclusivity/condition/cycle validation, normalization, and
ordering. `ApplicationLinkCompilationResult.Declarations` exposes complete,
resolved `ApplicationLinkDeclarationProjection` facts for persistence.
`ApplicationLinkCompiler.SerializeDeclarations(...)` writes their exact
`Port` / optional `Condition` representation.

Designer persistence maps those public projections instead of calling the
internal parser or rebuilding local/fully qualified references itself. Invalid
or partially valid declaration properties remain raw for lossless round-trip.
Engine owns its configuration-tree reader and uses the public Composition
resource-registration context. Composition no longer grants production friend
access to Designer or Engine; test-only friend access in unrelated packages is
unchanged.

Canonical JSON still has exactly two roots, in deterministic order:

```json
{
  "Resources": {},
  "Workflows": {}
}
```

Link declarations remain component properties. Do not add a root `Links`
collection, alternate root names, or a second persistence schema.

## Backend Registration Cleanup

FileSystem and SQL-file hosts now configure one canonical keyed factory through
a flat, synchronous builder callback:

```csharp
services.AddFluxFlowFileSystemStorage("items-store", storage =>
{
    storage.RootDirectory = "data/storage";
});

services.AddFluxFlowSqlFileStorage("audit-store", storage =>
{
    storage.DatabasePath = "data/audit.db";
});
```

Migrate advanced direct stores and custom factories to standard
`AddKeyedSingleton<IStorageStore>(...)` and
`AddKeyedSingleton<IStorageStoreFactory>(...)`. Session stores likewise use
standard keyed `ISessionStore` or `ISessionStoreFactory` registration. Use the
exact application resource address as the key. Direct stores remain shared and
host-owned; factory leases retain backend-specific ownership. Backend settings
do not move into `FluxFlowApplicationOptions`.

## Complete Migration Table

| Old surface | New surface or action | Source impact | Binary/package impact |
|---|---|---|---|
| `FluxFlow.Data` package/assembly | Reference `FluxFlow.Nodes` 4.0.0; keep `FluxFlow.Data` namespace imports | Package reference changes; type names do not | Defining assembly changes; rebuild required; old package removed |
| Family `*ComponentTypes` classes | `*ComponentDefinition.Types` | Update static member qualification | Old public type is removed from the adapter assembly |
| Family `*ComponentOptions`, `*ComponentPorts`, and `*ComponentResources` classes | Matching nested class on `*ComponentDefinition` | Update static member qualification | Old public types are removed from the adapter assembly |
| `IComponentDesignMetadataProvider` and family provider classes | Flat `AddComponent(...)` registrations | Replace provider implementation/consumption | Provider interface and 19 public provider classes are removed |
| `ComponentDesignMetadataModule` | Flat `AddComponent(...)` registrations | Replace module construction with one registration per component type | Module public type is removed |
| `AddComponentDesignMetadataProvider(...)` | `AddFluxFlowComponents().AddMapping()` or matching selected families | Change DI registration; no terminal call is required | Provider and declaration registration overloads are removed |
| `ComponentDesignMetadataCatalog.FromProviders(...)`, `FromDeclarations(...)`, `Add(...)`, and `AddRange(...)` | `new ComponentDesignMetadataCatalog(metadata)` for standalone tooling; normal DI registration is automatic | Construct one immutable snapshot or resolve it from DI | Mutable/factory catalog APIs are removed |
| Family `*ComponentDefinition.CreateMetadata()` | Resolve `ComponentDesignMetadataCatalog` after family registration | Remove metadata-only factory calls | All 19 metadata shims are removed |
| Independent Designer link parsing | `ApplicationLinkCompilationResult.Declarations` | Map public canonical projections | Additive Composition API; Designer 5 binary changed |
| Independent link declaration serialization | `ApplicationLinkCompiler.SerializeDeclarations(...)` | Serialize canonical projections | Additive Composition API; one wire grammar remains |
| Composition internals accessed by Designer/Engine | Public link projection/serializer and `ApplicationResourceRegistrationContext` constructor; Engine-owned configuration reader | Remove internal calls | Production friend grants removed; affected assemblies must rebuild |
| Root `Links`, `Composition`, `Nodes`, or Engine-specific wrapper documents | Exact root `Resources` and `Workflows`; links stay on component port properties | Convert persisted documents once outside runtime | Unsupported shapes remain rejected; no compatibility parser |
| Reflection/provider discovery expectations | Explicit family registrations and immutable catalog snapshot | Register every selected family deliberately | No scanning/discovery dependency or fallback package |
| Adapter-specific storage/session registration overload sets | One flat backend builder for built-in storage; standard keyed DI for custom storage and sessions | Replace helper calls and preserve exact resource keys | Breaking helper removal; no compatibility wrappers |

## Package Versions

The breaking surface reset advances these maintained package lines:

| Package or family | Major line |
|---|---:|
| `FluxFlow.Nodes` | 4 |
| `FluxFlow.Composition` | 6 |
| `FluxFlow.Engine` | 7 |
| `FluxFlow.Components.Designer` | 5 |
| `FluxFlow.Components.Observability` | 7 |
| Composition adapter packages | next major |
| `FluxFlow.Fluent` and `FluxFlow.Fluent.Hosting` | 4 |

The project-reference closure contains 51 affected retained packages and four
unaffected retained packages. Nodes is the only package whose project version
changes during this continuation (3.0.1 to 4.0.0). Every other affected package
was already on its intended current-reset major at the starting commit and is
deliberately not advanced a second time. Data is removed rather than bumped.
Use `eng/packages.json` and the complete shipped package index in
`docs/14-public-api-overview.md` as the authoritative version inventory.

## Migration Checklist

1. Replace forwarding hosting calls with `AddFluxFlow(...)` and
   `FluxFlowApplication`.
2. Convert and persist every legacy document outside the runtime.
3. Replace all retired component type names using the table above.
4. Rename counter option `expression` to `predicate`.
5. Replace registry usage with exact keyed-DI registrations and resolution.
6. Move support-package contracts into the host or an explicit adapter.
7. Replace Data package references with Nodes 4.0.0 and rebuild dependents.
8. Replace metadata providers and split identity classes with declarations and
   family component definitions.
9. Consume Composition link projections; do not introduce a second parser or
   root `Links` collection.
10. Update package major references and regenerate public API baselines.
11. Migrate built-in storage to the flat backend builders and custom/session
    stores to standard exact-key DI.
12. Run canonical parse, Designer, Engine, package, and consumer tests.

Do not recreate the removed compatibility layers in downstream applications.
