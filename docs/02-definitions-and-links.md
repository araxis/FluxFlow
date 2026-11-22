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
services.AddFluxFlowApplication(configuration);
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
          { "Address": "other.worker.Input", "Condition": "value.amount > 10",
            "Engine": "csharp" }
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
During DTO binding those strings become validated, full PortAddress values, resolved
relative to the component's workflow. Invalid address syntax fails binding with its
configuration location. Link registration receives typed addresses, not raw strings;
registered port existence and direction are checked later at Build.
Port property names are case-insensitive, including Open, Close, Unmatched and custom
route names. Their address values retain the registered spelling: `open: "clock.Output"`
selects the declared Open port, but `clock.output` does not alias `clock.Output`.
Each registrar explicitly maps its configuration properties to native ports and their
directions. There is no central inference from registered port names.
Bundled adapters register a named Events output with this same link syntax.
Addresses within a workflow may omit that workflow; cross-workflow addresses include it.

Link objects accept Address, Condition and Engine.
Address identifies the other port: the target when declared on Output, the source
when declared on Input. Rename the former Port field to Address; Port is rejected.
Engine is a keyed service name local to the condition; omission uses the host's
unkeyed default engine.
Constants belong directly in Condition; Parameters is no longer supported.
Links wait for destination capacity using native SendAsync. Acceptance does not mean
processing has finished or the message has been persisted. Delivery is no longer a
link option; remove that field from old configuration, including Delivery: Reliable.

Conditions filter independently: every matching link receives the message, and a link
without Condition always matches. When all conditions are false, the message is consumed
without downstream delivery or a routing error; it does not remain queued for a future
match. Registered outputs with no links also drain normally. Fallback is removed and
rejected even when false. Use an explicit condition if an additional branch is required;
an unconditional link receives every message, not just unmatched ones.
An exception while evaluating a condition or a target refusing admission faults execution.

Each component registrar registers link delegates from its bound DTO after its ports. No
application-wide document scanner runs during Build. Malformed declarations reject
registration transactionally; equal links declared from both ends are registered once.
Build resolves ports,
compiles conditions once per provider and validates runtime routing before Start.
See [conditions](51-expression-conditions.md) and [registration](40-component-registration.md).

## Error links

Bundled configurations expose Error using the same string/object/array syntax:

```json
"map": {
  "Type": "mapper", "Engine": "jsonata", "Expression": "payload",
  "Input": "source.Output",
  "Output": "next.Input",
  "Error": ["log.Input", { "Address": "alerts.Input", "Condition": "$value.isTransient = false", "Engine": "jsonata" }],
  "Events": "diagnostics.Input"
}
```

Error selects messages whose envelope IsError is true from the existing Output.
It is configuration sugar for a filtered link, not a new port: `map.Error` is not
an address. An optional Condition runs only for errors and receives the original
message context. Without Condition, no expression engine is needed. Output retains
its existing behavior, including errors: an error can go to both Output and Error
destinations. Registering both selections to the same destination can deliver an error
twice; repeating an identical Error declaration still creates only one link.
There is no automatic error collector, logging component or runtime component change.
