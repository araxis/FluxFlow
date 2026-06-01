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
4. Rewrite detailed public docs from the legacy reference set. Initial focused
   set plus validation/errors, runtime-state, JSON conversion, expression
   mapping, and package versioning references done.
5. Keep release automation healthy for the next prerelease.
6. Plan the first component package template. Started with the MQTT package
   template plan and added a component category catalog. The component catalog
   now uses package-owned request/options/result contracts and treats the first
   consumer as validation, not as the source of reusable schemas.
7. Scaffold the first MQTT component package. Done with adapter-backed publish
   and subscribe nodes plus deterministic tests.
8. Scaffold the first generic mapping component package. Done with `flow.mapper`,
   typed ports, host-provided expression engines, context factories, diagnostics,
   and per-message failure handling.
9. Scaffold the first generic control component package. Done with `flow.filter`,
   `flow.when`, and `flow.assert` as expression-driven, typed control nodes.

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

Each component family can become a separate package after the base engine is
stable and a consumer pilot proves the extension surface.
Each component family should also be a separate source project in the solution
and produce its own package artifact.

Possible packages:

- `FluxFlow.Components.Data`
- `FluxFlow.Components.Control`
- `FluxFlow.Components.Http`
- `FluxFlow.Components.Mapping`
- `FluxFlow.Components.Mqtt`
- `FluxFlow.Components.Files`
- `FluxFlow.Components.Replay`
- `FluxFlow.Components.Timers`
- `FluxFlow.Components.Validation`
- `FluxFlow.Components.Diagnostics`

Package rules:

- Each component family is a separate source project.
- Each component family produces a separate package artifact.
- Each package owns its node registrations.
- Each package owns options, request/result records, validation, diagnostics
  names, events, and tests.
- `Input` is the default inbound port; typed request records carry semantic
  meaning for sink and command nodes.
- Options describe static node settings; requests describe per-message work.
- Consumer application schemas must not leak into reusable package contracts.
- The engine stays protocol-neutral.
- Start with one package, likely MQTT, as the template before creating several.

First template plan:

- `FluxFlow.Components.Mqtt`
- adapter contracts and request/options records first, no live client dependency
  in the initial template
- deterministic tests with an in-memory adapter
- explicit `IFlowNodeModule` registration
- release workflow update to process multiple independent package projects

Status: initial package implemented with adapter-backed publish and subscribe
nodes.

Second package:

- `FluxFlow.Components.Mapping`
- starts with `flow.mapper` only
- keeps expression engines and context building host-provided
- supports object defaults and host-registered typed ports
- reports per-message failures without stopping later messages

Third package:

- `FluxFlow.Components.Control`
- started with `flow.filter`, `flow.when`, and `flow.assert`; `flow.assert`
  later moved to `FluxFlow.Components.Assertions`
- keeps expression engines and context building host-provided
- supports object defaults and host-registered typed ports
- avoids scenario/test-runner behavior
- reports per-message expression failures without stopping later messages

Package category decision options:

- MQTT first for real source/sink integration pressure.
- Timers first for fastest template proof.
- Files first for a balanced source/sink package without broker dependencies.
