# FluxFlow.Components.Sources.Composition

Optional runtime registration and definition for generated and sequence sources.

Canonical FlowMessage output carries generated JSON values or sequence items.
Item/sequence, timing and emission settings remain flat. Both nodes expose Output
and diagnostic Events; the optional clock is host-owned.

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

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddSources();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddGeneratedSource`, `AddSequenceSource` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former SourcesComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.
