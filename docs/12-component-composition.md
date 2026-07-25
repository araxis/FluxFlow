# Component Composition

FluxFlow applications use the immutable
`FluxFlow.Composition.Model.ApplicationDefinition` as the canonical executable
document. Its JSON root contains exactly `Resources` and `Workflows`.
Workflows contain component objects directly; a component object key is its
runtime identity, and `Type` selects its registered factory.

Component packages own reusable request/result behavior. Hosts own resources,
concrete clients, credentials, stores, lifecycle policy, and application UI.
`FluxFlow.Engine.Hosting` may activate the canonical model, but component
packages remain independent from the Engine.

## Recommended Path

1. Register component factories explicitly in `CompositionNodeRegistry`.
2. Load and normalize one canonical `ApplicationDefinition`.
3. Compile links before preparing a runtime revision.
4. Use ordinary output fan-out when several targets need the same data.
5. Link several compatible outputs to one shared input for fan-in.
6. Put decisions on links with `Condition` instead of adding structural routing
   components.
7. Insert a mapper only when payload types or shapes must change.
8. Observe component diagnostics through `Workflow.Component.Events`.

`flow.filter`, `flow.when`, `flow.switch`, `flow.fork`, and `flow.merge` remain
loadable and renderable for existing definitions. Do not introduce them in new
workflow guidance. Keep `flow.window`, `flow.correlate`, and `flow.join` when
they provide actual stateful behavior rather than graph structure.

## Flat Document

```json
{
  "Resources": {
    "Processing": {
      "ParallelOrdered": {
        "Type": "processing.profile",
        "Mode": "Parallel",
        "Order": "Preserve",
        "Buffer": "Standard"
      }
    },
    "External": {
      "ApiClient": {
        "Type": "host.http-client"
      }
    }
  },
  "Workflows": {
    "Orders": {
      "Normalize": {
        "Type": "data.map",
        "Expression": "payload"
      },
      "Validate": {
        "Type": "json.validate",
        "Input": "Normalize.Output"
      },
      "Send": {
        "Type": "http.request",
        "Client": "Resources.External.ApiClient",
        "Processing": "Resources.Processing.ParallelOrdered",
        "Input": {
          "Port": "Validate.Output",
          "Condition": "payload.isError = false"
        }
      },
      "RecordFailure": {
        "Type": "session.record",
        "Input": {
          "Port": "Validate.Output",
          "Condition": "payload.isError = true"
        }
      },
      "ObserveValidation": {
        "Type": "log.write",
        "Input": "Validate.Events"
      }
    }
  }
}
```

One link may be a string. Multiple links use an array. A conditioned link uses
an object with `Port` and `Condition`. A port property may appear on the input
or output endpoint, but the same endpoint pair must be declared only once.
Local `Component.Port` references resolve inside the current workflow; absolute
`Workflow.Component.Port` references use the same address framework across
workflows.

## Fan-Out And Fan-In

Declare output fan-out directly:

```json
{
  "Type": "source.items",
  "Output": ["Validate.Input", "Audit.Input"]
}
```

Declare shared-input fan-in from the input side:

```json
{
  "Type": "data.map",
  "Input": ["Primary.Output", "Replay.Output"]
}
```

Dataflow links do not independently complete a shared input. The runtime
completes it after every upstream succeeds, and faults it once when the first
upstream faults.

## Output, Events, And Completion

Canonical components use these channels consistently:

| Surface | Meaning |
|---------|---------|
| `Output` | Normal success and expected failure data, normally `FlowResult<T>`. |
| `Events` | Traced lifecycle, diagnostic, observation, warning, and metric data. |
| `Completion` | Unrecoverable implementation, infrastructure, or lifecycle failure. |

Do not add a universal `Errors` port. Expected errors are ordinary result data
and can be mapped, conditioned, recorded, retried, or sent to another workflow.
Legacy and typed compatibility registrations may retain released error streams.

Every canonical registration reserves `Events` as an output carrying
`FlowMessage<CompositionComponentEvent>`. The address
`OrderProcessing.ValidateOrder.Events` participates in links and direct runtime
observation like any other output. Its message envelope remains the authority
for trace, correlation, message, and causation identity.

Component events are not copied into `System.Events.Output`. The latter is the
Engine-owned application/revision event stream, so observing both does not
produce duplicate component events.

## Component Identity

The component object key is the canonical name. In this example, `Validate` is
the component identity; a separate `Name` option is unnecessary. Canonical
factory binding defaults an absent options `Name` from that key. Explicit
legacy `Name` values remain accepted during migration, and `DisplayName` stays
a Designer/UI concern.

## Processing Profiles

Normal canonical JSON does not expose `BoundedCapacity`,
`MaxDegreeOfParallelism`, or `EnsureOrdered`. Defaults require no profile.
When policy must be reusable, define a `processing.profile` resource with:

- `Mode`: `Sequential` or `Parallel`.
- `Order`: `Preserve` or `Relaxed`.
- `Buffer`: `Small`, `Standard`, or `Large`.

Reference it with one flat `Processing` property. The host may replace
`ICompositionProcessingProfileMapper` in DI to choose technical values.
Component registrations declare supported concurrency; stateful or strictly
ordered components reject unsupported profiles before factory execution.
Standalone C# options retain their technical properties for direct-code and
advanced compatibility scenarios.

## Normalization And Compatibility

`ApplicationDefinitionNormalizer` runs after load and before validation,
revision comparison, Designer projection, or runtime preparation. It rewrites
registered component aliases and known resource aliases, returns structured
migration diagnostics, and is deterministic and idempotent. Designer saves
always emit canonical names, and alias-only revisions compare as unchanged.

Package-internal typed descriptors are the single source for canonical type
names and aliases. Public type constants remain available. Registration and
metadata use explicit code only: no reflection, assembly scanning, source
generation, or global discovery.

## Host Boundary

The host owns:

- canonical definition loading, versioning, validation, and persistence
- concrete clients, stores, clocks, credentials, secrets, and disposal
- expression engines and processing-profile mapping
- resource catalogs and keyed DI registration
- revision activation, direct port access, dashboards, and Designer rendering

Component packages own neutral contracts, options, standalone nodes, factory
registrations, and package-authored metadata. They do not own application file
formats, renderer UI, host resources, or global orchestration.

## Migration Boundary

Version 3 removes the parallel Composition definition and runtime families.
`ComponentDefinition`, component object keys, canonical properties, and the
application revision host are the sole maintained path. Existing
workflows/nodes/links documents can be converted with
`LegacyCompositionDefinitionMigrator`; normal runtime loading never accepts
that retired shape.
