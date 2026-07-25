# FluxFlow.Components.Control.Composition

Migration package for the removed `flow.filter` and `flow.when` registrations
and Designer metadata. Version 3 contains no node registrations or metadata
providers. Canonical definitions put conditions directly on links.

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
      "Accepted": { "Type": "orders.accepted" },
      "Rejected": { "Type": "orders.rejected" }
    }
  }
}
```

Migrate definitions and remove calls to `RegisterFilter<T>()` and
`RegisterWhen<T>()` before upgrading. Once no legacy definitions remain,
remove this package reference. Component settings and links stay flat;
addresses are exact and case-sensitive.
