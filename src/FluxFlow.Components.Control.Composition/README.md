# FluxFlow.Components.Control.Composition

Compatibility `FluxFlow.Composition` registrations and Designer metadata for
the obsolete `flow.filter` and `flow.when` nodes. Canonical definitions use
conditions directly on links instead of structural control components.

## Canonical Definition

```json
{
  "Resources": {},
  "Workflows": {
    "Orders": {
      "Source": {
        "Type": "orders.source",
        "Output": [
          {
            "Port": "Accepted.Input",
            "Condition": "payload.accepted == true"
          },
          {
            "Port": "Rejected.Input",
            "Condition": "payload.accepted != true"
          }
        ]
      },
      "Accepted": {
        "Type": "orders.accepted"
      },
      "Rejected": {
        "Type": "orders.rejected"
      }
    }
  }
}
```

Use one conditioned link to filter and complementary conditioned links to
branch. Component settings and links remain flat, addresses are exact and
case-sensitive, and the link compiler owns compile-once condition validation.

## Legacy Registration

The released factories remain available for existing definitions:

```csharp
#pragma warning disable CS0618
registry
    .RegisterFilter<OrderMessage>()
    .RegisterWhen<OrderMessage>();
#pragma warning restore CS0618
```

| Type | Required resource | Ports |
|------|-------------------|-------|
| `flow.filter` | `engine` | `Input`, `Output` |
| `flow.when` | `engine` | `Input`, `WhenTrue`, `WhenFalse`, `Output` |

The factories still resolve a host-owned keyed `IFlowExpressionEngine`, an
optional typed `IFlowMapContextFactory<TInput>`, and an optional
`TimeProvider`. They preserve all released options, diagnostics, aliases,
Errors ports, and activation validation. Use custom node type names when a
legacy host needs several CLR input shapes.

## Design Metadata

`ControlComponentDesignMetadataProvider` retains complete option, port, and
resource metadata so existing documents remain readable. Both entries set
`deprecated=true` and provide canonical-link migration guidance. Hosts should
hide deprecated entries from new-node palettes while still rendering and
validating existing nodes.

The metadata is descriptive only. Hosts continue to own resource registration,
lifetime, rendering, validation UI, and persistence.
