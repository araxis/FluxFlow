# Serialization presentation separation

Implementation checkpoint in the approved core refactoring; not release approval.

| Package | Responsibility | Targets |
|---|---|---|
| FluxFlow.Components.Serialization | Existing directly constructed conversion nodes; unchanged | net8.0;net10.0 |
| FluxFlow.Components.Serialization.Composition | Existing runtime contracts and portable authoring; Designer dependency removed | net8.0;net10.0 |
| FluxFlow.Components.Serialization.Designer | New opt-in labels, defaults, editor hints and resource pickers | net8.0;net10.0 |
| FluxFlow.Components.Designer | Generic presentation projection and decoration of registered structural facts | net8.0;net10.0 |

This creates one real optional integration boundary for the first migrated family,
not a new execution or abstract contract layer. The runtime and authoring APIs stay
in their current packages; consumers needing only them do not restore Designer.
Other module families remain explicitly unmigrated at this checkpoint.

## Consumer migration

Runtime registration remains `services.AddFluxFlowComponents().AddSerialization()`.
It no longer registers a design catalog. Editor consumers additionally reference
`FluxFlow.Components.Serialization.Designer`, import its namespace, and call:

```csharp
services.AddFluxFlowComponents().AddSerialization().AddSerializationDesign();
```

All six conversions retain their runtime factories, input shapes, options, optional
borrowed clock and canonical Input/Output/Events. Conversion implementations and
stored workflow JSON formats do not change. Existing visual labels, defaults and
hints move to the optional package. The change belongs to the existing unreleased
major-update train; the new optional package starts at 1.0.0-rc.1. Final local feed
versions are still chosen together at the complete-candidate gate.

## Extension boundary

`AddDesignMetadata(type, describe)` accepts a presentation transformation over the
already registered descriptor. It does not register or activate a component. Port
names, directions, message types, input shapes, cardinality, required options and
resources, type identity and processing capabilities cannot be changed by the
decorator. Labels, ordering, defaults, editor hints and event descriptions are
presentation. The resulting catalog owns a frozen snapshot, with existing duplicate
and conflict checks. Static projection is shared with the older designed-registration
path while the remaining families migrate; no new factory hierarchy was introduced.

The optional registration builder still contains the remaining authoring activation
machinery. Removing one Designer dependency does not prove the full creator/port/
authoring migration complete. Full performance and fresh isolated package-consumer
acceptance remain required; nothing is published by this checkpoint.
