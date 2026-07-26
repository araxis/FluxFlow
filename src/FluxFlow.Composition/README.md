# FluxFlow.Composition

Canonical application definitions, explicit component registration, addressing,
validation, link compilation, and code-first runtime ownership. The package is
Engine-independent.

## Application Shape

```json
{
  "Resources": {},
  "Workflows": {
    "Orders": {
      "Receive": { "Type": "source" },
      "Handle": {
        "Type": "handler",
        "Input": "Receive.Output"
      }
    }
  }
}
```

Resources, workflows, and components are named by object keys. Components are
flat; there are no maintained Composition, Nodes, or root Links wrappers.
Addresses are ordinal and case-sensitive. Links support fan-in, fan-out,
conditions, cross-workflow addresses, and explicit bounded signal feedback.
Ordinary data-processing cycles are rejected.

Registrations declare the same typed `FlowMessage<T>` ports used by their node,
flat options, host-owned resources, Events, aliases, and Designer metadata.
Errors travel on normal outputs. Composition owns node/link lifecycle but does
not own host resources supplied through DI.

Canonical workflow JSON selects an optional semantic `Processing` profile.
Composition maps that profile centrally to capacity, parallelism, and ordering.
Direct C# callers may still provide the technical options explicitly; those
compatibility settings are not primary workflow or Designer concepts.

`CompositionRuntime` waits for all upstreams before completing a shared input,
faults fan-in once on the first upstream fault, and attempts all cleanup before
aggregating teardown failures.
