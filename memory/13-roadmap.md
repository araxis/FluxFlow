# Roadmap

Date: 2026-05-31

## Current Direction

Proceed with migration polish after the first real consumer proved the package
boundary.

FluxMq has migrated its runtime dependency to `FluxFlow.Engine`
`0.4.0-alpha.1`. Keep the FluxMq branch read-only from this repository while
its current feature work settles, then use it as the source for cleanup and
package-authoring improvements.

## Near-Term Work

1. Add a neutral consumer sample that mirrors the FluxMq integration shape. Done.
2. Review package-authoring ergonomics exposed by the FluxMq factory registry. Done.
3. Decide whether `0.5.0-alpha.1` should include only samples/docs or a small
   registration helper. Done and published.
4. Rewrite detailed public docs from the legacy reference set.
5. Keep release automation healthy for the next prerelease.

## First Consumer Pilot

FluxMq now validates the intended engine boundary:

1. The app keeps its own workspace schema and projects executable resources and
   workflows into `ApplicationDefinition`.
2. The app host wraps `ApplicationRuntimeBuilder` for FluxMq-specific loading,
   build-result mapping, scenarios, and MQTT client resolution.
3. Components remain outside the engine and register through the runtime factory
   registry.
4. Stale FluxMq docs and build-output folders remain as FluxMq-side cleanup.

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
- `FluxFlow.Components.Mqtt`
- `FluxFlow.Components.Files`
- `FluxFlow.Components.Replay`
- `FluxFlow.Components.Timers`
- `FluxFlow.Components.Validation`
- `FluxFlow.Components.Diagnostics`

Package rules:

- Each package owns its node registrations.
- Each package owns options, validation, diagnostics names, events, and tests.
- The engine stays protocol-neutral.
- Start with one package, likely MQTT, as the template before creating several.
