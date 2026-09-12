# FluxFlow.Components.Sources.Composition

Optional runtime registration and authoring for generated and sequence sources.

Canonical FlowMessage output carries generated JSON values or sequence items.
Item/sequence, timing and emission settings remain flat. Both nodes expose Output
and diagnostic Events; the optional clock is host-owned.

Editor hints are separate: install `FluxFlow.Components.Sources.Designer` and call
`AddSourcesDesign()` after `AddSources()`. Runtime registration alone does not
register a presentation catalog, activate components or resolve host resources.

## DI Registration

For a direct Core application built from a document, bind the source types to
their ordinary constructors:

```csharp
var bindings = new ComponentBindings().AddSources(clock);
bindings.Add("app.consumer", definition => new Consumer(options));
await using var application = await FlowJson.BuildAsync(json, bindings);
await application.StartAsync();
await application.Completion;
```

Here `Consumer` is the application's own component. No catalog, descriptor,
activation context or component creator is used by these bindings. The generated
binding and the retained catalog binding share `GeneratedSourceNode.Create`.

Example source declaration inside the required Resources/Workflows document:

```json
{
  "Resources": {},
  "Workflows": {
    "main": {
      "numbers": { "Type": "source.sequence", "name": "numbers", "start": 2, "step": 2, "count": 5, "Output": "consumer.Input" },
      "consumer": { "Type": "app.consumer" }
    }
  }
}
```

Omitting `name` retains the standalone default (`sequence` or `generated`), not
the graph component address. Clocks are borrowed: an explicit `clock` string
requires `AddSources(clock, resolveClock: key => ...)`; missing or invalid
references fail instead of silently using the default. Document resource
declarations and processing-profile references are not yet migrated on this
direct path. Use explicit options/dependencies, or the retained update path for
those features. Diagnostic events use Core's separate Events stream; named event
business links remain on the update path during migration.

### Catalog and update hosting

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit SourcesComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddSources();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`SourcesComponents` exposes `GeneratedSource` and `SequenceSource` typed contracts. The retained `AddX` methods use those same contracts, and both handles expose `Output` and `Events`. See [typed code-first authoring](../../../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.
