# Changelog

## 0.3.0-alpha.1

Neutral event metadata naming.

- Renamed `FlowEvent.Topic` to `FlowEvent.Channel`.
- Renamed the `EventFlowNodeBase.EmitEvent(... topic ...)` helper parameter
  to `channel`.
- Kept channel as first-class event metadata instead of moving it into
  attributes.

## 0.2.0-alpha.1

Engine-only prerelease boundary.

- Removed scenario/test definitions from `ApplicationDefinition`.
- Removed scenario validation from the engine definition validator.
- Removed scenario runner APIs from `FlowApplicationHost`.
- Kept runtime events and diagnostics as the generic observation surface.
- Documented that applications should project their own workspace models into
  executable engine resources and workflows.

## 0.1.0-alpha.1

Initial prerelease of `FluxFlow.Engine`.

- Protocol-neutral workflow definition model.
- Typed node input and output ports.
- Runtime graph builder with phase-ordered lifecycle.
- Reliable runtime fanout for linked inputs.
- Node authoring helpers for source, sink, transform, map, events, diagnostics,
  and registration.
- Runtime, workflow, host, event, error, and diagnostics streams.
- Generic scenario runner with event expectations.
- DynamicExpresso and JSONata expression adapters.
- NuGet packaging with README, symbols, repository metadata, and MIT license.
