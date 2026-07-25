# FluxFlow.Components.Control

Migration package for the removed `FilterNode<T>` and `WhenNode<T>` APIs.
Version 5 contains no runtime nodes. Canonical workflow links own filtering and
branching, so new applications do not need this package.

## Migration

A filter is one conditioned link. A true/false branch is two links with
complementary conditions:

```json
{
  "Resources": {},
  "Workflows": {
    "Orders": {
      "Normalize": {
        "Type": "data.map",
        "Output": [
          {
            "Port": "Priority.Input",
            "Condition": "payload.priority == 'High'"
          },
          {
            "Port": "Standard.Input",
            "Condition": "payload.priority != 'High'"
          }
        ]
      },
      "Priority": { "Type": "orders.priority" },
      "Standard": { "Type": "orders.standard" }
    }
  }
}
```

Composition compiles each distinct condition once per activation. A condition
failure rejects only that link, preserves message identity in the resulting
diagnostic and system event, and does not stop sibling links or the host.

Migrate all `FilterNode<T>` and `WhenNode<T>` usages before upgrading, then
remove the package reference. Use an explicit mapper before conditioned links
when routing requires a new payload shape.

## Composition

`FluxFlow.Components.Control.Composition` version 3 is also migration-only and
contains no `FluxFlow.Composition` factories or Designer metadata. Migrate
`flow.filter` and `flow.when` definitions before upgrading, then remove both
package references.
