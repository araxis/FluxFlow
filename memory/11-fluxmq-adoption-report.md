# FluxMq Adoption Report

Date: 2026-05-31

## Status

`FluxFlow.Engine` is usable as the generic runtime for `FluxMq`, but the migration is not a drop-in reference swap yet.

The extracted package now owns:

- application and workflow definitions;
- typed input and output ports;
- runtime build, start, stop, fault, and disposal lifecycle;
- reliable runtime fanout;
- generic event collection;
- generic mapping contracts and expression engines;
- base node authoring helpers;
- runtime diagnostics for app-owned observability and test layers.

`FluxMq` should keep:

- MQTT connection, trigger, publisher, recorder, and metrics components;
- file writer, assertion, schema validation, payload inspector, replay, and stored-session components;
- UI component catalog, node models, widgets, and dashboard behavior;
- MQTT-specific event type constants;
- MQTT-specific scenario steps and validation;
- package or app-level factory registration for FluxMq node types.

## Current overlap

`FluxMq.Pipeline` currently contains 75 C# files and about 3,989 lines:

- Components: 6 files, 74 lines.
- Definitions: 20 files, 1,541 lines.
- Mapping: 9 files, 157 lines.
- Runtime: 19 files, 1,065 lines.
- Scenarios: 20 files, 1,092 lines.
- Root: 1 file, 60 lines.

Most of this is now owned by `FluxFlow.Engine`.

## Expected migration shape

1. Add `FluxFlow.Engine` to `FluxMq` by project reference first, then switch to a package reference after the first stable prerelease is published.
2. Replace `FluxMq.Pipeline.*` usings with `FluxFlow.Engine.*` namespaces in `FluxMq.App`, `FluxMq.Components`, `FluxMq.UI`, `FluxMq.Cli`, and tests.
3. Move FluxMq-specific constants out of `FluxMq.Pipeline` before removing that project:
   - `PipelineFlowNodeTypes` should become a FluxMq app/components catalog type.
   - `FlowEventTypes` should become a FluxMq components type.
   - MQTT scenario step types should become FluxMq app scenario types.
4. Update FluxMq component implementations so they implement `FluxFlow.Engine.Components.IFlowNode` and use `FluxFlow.Engine.Core.FlowNodeId`, `FlowError`, and `FlowEvent`.
5. Rewrite `FluxMq.App.RuntimeNodeFactoryRegistryExtensions` to use FluxFlow node factory APIs. The current registration shape can stay mostly the same, but the new builder helpers can reduce repeated `InputPort` and `OutputPort` construction.
6. Replace or wrap `FluxMq.App.FlowApplicationHost` with `FluxFlow.Engine.FlowApplicationHost`. Keep a thin FluxMq host facade only for default FluxMq factory/scenario registration.
7. Keep scenario documents, result reporting, and MQTT-specific scenario runners in FluxMq or a FluxMq-owned testing package.
8. Delete `src/FluxMq.Pipeline` and `tests/FluxMq.Pipeline.Tests` after all references are removed and equivalent coverage lives in FluxFlow or FluxMq-specific tests.

## Estimated impact

Direct references to `FluxMq.Pipeline` exist in about 166 files:

- 127 source files.
- 39 test files.

The largest code changes are namespace and project-reference changes. The risky changes are type identity changes for:

- `FlowNodeId`;
- `FlowError`;
- `FlowEvent`;
- `ApplicationDefinition` and related definition records;
- scenario step constants and validators.

The current package can remove about 3,000 to 3,500 lines from FluxMq after MQTT-specific scenario pieces and constants are moved out.

## Suggested order

Do the migration in small slices:

1. Add FluxFlow reference and compile side-by-side.
2. Move FluxMq-specific constants and scenario helpers out of `FluxMq.Pipeline`.
3. Migrate `FluxMq.Components`.
4. Migrate `FluxMq.App` factory registration and host.
5. Migrate UI and CLI usings/models.
6. Remove `FluxMq.Pipeline`.
7. Run the full FluxMq test suite.

## Notes

The package README now describes reliable owned fanout. FluxMq migration should
wait until current FluxMq feature work settles, then start with one small
consumer slice before replacing the old pipeline project.
