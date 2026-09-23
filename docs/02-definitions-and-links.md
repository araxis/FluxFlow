# Configuration and links

FluxFlow.Configuration traverses IConfigurationSection directly. .NET configuration
providers supply the hierarchy; FluxFlow does not build an intermediate document tree.
The root represents the application, with Workflows and optional Resources.
Each workflow contains component sections and optional Resources. Each component
or resource type owns its data-only configuration DTO; no base configuration class
is required. Its registration delegate binds the section and registers a deferred
synchronous factory, with native ports identified by ApplicationAddress.

```csharp
// Register component adapters and expression engines first.
await services.AddFluxFlowApplicationAsync(configuration);
await using var provider = services.BuildServiceProvider();
await using var application = await provider.BuildApplicationAsync();
await application.StartAsync();
// Stop before the host disposes its provider.
await application.StopAsync();
```

Use configuration.GetSection("Flows") when the host chooses a nested application root.
Registration creates no components and no provider. Build resolves resources,
components, native ports and LinkFactory delegates from the host provider.

## Flat links

```json
{
  "Workflows": {
    "main": {
      "source": {
        "Type": "sample.source",
        "Output": [
          "audit.Input",
          { "Port": "other.worker.Input", "Condition": "value.amount > minimum",
            "Engine": "csharp", "Parameters": { "minimum": 10 } }
        ]
      },
      "audit": { "Type": "sample.sink" }
    },
    "other": { "worker": { "Type": "sample.worker" } }
  }
}
```

The host must register the sample types and the engine. An input may declare its
source with Input: "source.Output" instead. Both directions accept a single string,
an object or an array mixing them. Named registered ports use the same syntax.
Addresses within a workflow may omit that workflow; cross-workflow addresses include it.

Link objects accept Port, Condition, Engine, Parameters, Delivery and Fallback.
Engine is a keyed service name local to the condition; omission uses the host's
unkeyed default engine. Fallback cannot contain a condition, engine or parameters.
Delivery accepts Reliable, RejectWhenFull or IsolateRejection; the latter requires
the runtime rejection handler contract and is not automatically configured by setup.

Each component section registers link delegates immediately after its ports. No
application-wide document scanner runs during Build. Malformed declarations and
conflicting duplicates reject registration transactionally. Build resolves ports,
compiles conditions once per provider and validates runtime routing before Start.
See [conditions](51-expression-conditions.md) and [registration](40-component-registration.md).
