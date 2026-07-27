# Major Surface Reset Migration

This guide covers the current breaking reset of obsolete hosting, document,
alias, registry, and disconnected support-package surfaces. The runtime now has
one canonical path: register Engine directly, load canonical application
definitions, and resolve host-owned services through dependency injection.

## Runtime And Hosting

Replace forwarding hosting APIs with Engine's maintained entry points:

```csharp
services.AddFluxFlow(options =>
{
    options.ApplicationName = "orders";
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
Register `IFlowExpressionEngine` and `IFlowMapContextFactory<TInput>` through
the package's keyed-DI helpers or through the host container directly. Resolve
the exact key and exact generic service type. There is intentionally no
replacement global registry, default-engine fallback, assignable-type search,
or custom resolver layer.

## Removed Support Packages

The disconnected resource, secrets, configuration, and journal component
packages were removed. They were not part of the executable component catalog
or Engine lifecycle. Move any consumer-owned contracts into the host or an
explicit adapter package, and register the resulting keyed resources through
`IApplicationResourceRegistrar` or standard dependency injection.

## Package Versions

The breaking surface reset advances these maintained package lines:

| Package or family | Major line |
|---|---:|
| `FluxFlow.Composition` | 6 |
| `FluxFlow.Engine` | 7 |
| `FluxFlow.Components.Designer` | 5 |
| `FluxFlow.Components.Expressions` | 3 |
| `FluxFlow.Components.Observability` | 7 |
| Composition adapter packages | next major |
| `FluxFlow.Fluent` and `FluxFlow.Fluent.Hosting` | 4 |

Use `eng/packages.json` as the authoritative package/version inventory.

## Migration Checklist

1. Replace forwarding hosting calls with `AddFluxFlow(...)` and
   `FluxFlowApplication`.
2. Convert and persist every legacy document outside the runtime.
3. Replace all retired component type names using the table above.
4. Rename counter option `expression` to `predicate`.
5. Replace registry usage with exact keyed-DI registrations and resolution.
6. Move support-package contracts into the host or an explicit adapter.
7. Update package major references and regenerate public API baselines.
8. Run canonical parse, Designer, Engine, package, and consumer tests.

Do not recreate the removed compatibility layers in downstream applications.
