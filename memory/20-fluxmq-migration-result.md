# FluxMq Migration Result

Date: 2026-05-31

## Status

FluxMq has migrated its runtime dependency to the published package
`FluxFlow.Engine` version `0.4.0-alpha.1`.

Observed package references:

- `src/FluxMq.App/FluxMq.App.csproj`
- `src/FluxMq.Components/FluxMq.Components.csproj`
- `src/FluxMq.Scenarios/FluxMq.Scenarios.csproj`

The FluxMq solution no longer lists `FluxMq.Pipeline` as a project. The old
`src/FluxMq.Pipeline` and `tests/FluxMq.Pipeline.Tests` directories still exist
only as build-output leftovers. FluxMq public docs still mention the old
pipeline name and should be cleaned during the FluxMq branch cleanup.

## Observed Shape

FluxMq now keeps app-specific ownership around the package:

- `FluxMqApplicationDefinition` owns FluxMq workspace sections such as
  resources, workflows, dashboards, and tests.
- `FluxMqApplicationDefinition.ToEngineDefinition()` projects only executable
  resources and workflows into `FluxFlow.Engine.Definitions.ApplicationDefinition`.
- `FlowApplicationHost` remains a FluxMq facade over `ApplicationRuntimeBuilder`.
  It owns FluxMq configuration loading, build-result mapping, scenario execution,
  and MQTT client wiring.
- `RuntimeNodeFactoryRegistryExtensions.RegisterPipelineComponentFactories()`
  registers FluxMq component factories against the engine runtime registry.
- `FluxMq.Components` owns MQTT, replay, file, validation, logging, assertion,
  filter, router, mapper, and metrics components.
- `FluxMq.Scenarios` owns scenario runners and event expectations.

This validates the intended boundary: the engine owns definitions, runtime,
ports, diagnostics, events, fanout, and conditional links; the application owns
its workspace schema, domain components, dashboards, and scenarios.

## Migration Friction

The migration also exposed practical package-design needs:

- Component registration is currently centralized in a large FluxMq extension
  method. Future component packages should provide smaller package-owned
  registration entry points.
- Each component factory manually creates ports and runtime nodes. The existing
  helper base classes reduce component code, but package authors still need a
  clear registration convention.
- The FluxMq host facade is still justified because it owns workspace schema,
  scenarios, build-result mapping, and MQTT client resolution.
- FluxMq docs still need cleanup to remove stale old-pipeline references.

## Next Steps

1. Keep the FluxMq migration branch read-only from this repository until the
   current FluxMq feature work is ready to merge.
2. Clean FluxMq docs and build-output leftovers in the FluxMq repository.
3. Add a neutral consumer sample in FluxFlow that mirrors the FluxMq integration
   shape without FluxMq-specific components.
4. Use the FluxMq factory registry as the source material for the first
   component-package registration convention.
5. Plan the next prerelease around migration polish and package-authoring
   ergonomics.
