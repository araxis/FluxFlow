# Roadmap

Date: 2026-05-31

## Current Direction

Proceed with release-readiness work for the base engine first.

The FluxMq migration is deferred until the current FluxMq feature work settles.
When that work is ready, use FluxMq as the first real consumer pilot.

## Near-Term Work

1. Finish the release-readiness pass.
2. Decide the release version and license metadata.
3. Rewrite enough public docs for a useful prerelease package.
4. Publish the first prerelease package. Done with `0.1.0-alpha.1`.
5. Publish the engine-only boundary prerelease after scenario/test ownership is removed.

## Deferred Consumer Pilot

After the current FluxMq feature work is complete:

1. Add `FluxFlow.Engine` to FluxMq by project reference.
2. Migrate one small vertical slice first.
3. Use that pilot to discover missing extension points.
4. Move to a package reference after the base package works in that slice.

## Future Fluent DSL

Add a C# fluent DSL for defining workflow applications without writing JSON by
hand. The goal is a pleasant authoring surface over the existing definition
model, not a second runtime.

Possible shape:

```csharp
var app = FlowApp.Define()
    .Workflow("main", flow => flow
        .Node("source", "demo.source")
        .Node("map", "demo.map", node => node
            .Input("Input", "source.Output"))
        .Node("sink", "demo.sink", node => node
            .Input("Input", "map.Output")))
    .Build();
```

Design rules:

- Keep JSON and object definitions as the core contract.
- The DSL should build `ApplicationDefinition`.
- The DSL should be optional.
- Prefer compile-time clarity over hidden conventions.
- Add only after the current API is stable enough.

Likely package name:

- `FluxFlow.Fluent`

## Future Component Packages

Keep components outside `FluxFlow.Engine`.

Each component family can become a separate NuGet package after the base engine
is stable and a consumer pilot proves the extension surface.

Possible packages:

- `FluxFlow.Components.Data`
- `FluxFlow.Components.Http`
- `FluxFlow.Components.Files`
- `FluxFlow.Components.Timers`
- `FluxFlow.Components.Diagnostics`

Package rules:

- Each package owns its node registrations.
- Each package owns options, validation, diagnostics names, events, and tests.
- The engine stays protocol-neutral.
- Start with one small package as the template before creating several.
