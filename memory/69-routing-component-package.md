# Routing Component Package

Date: 2026-06-02

## Decision

Add `FluxFlow.Components.Routing` as a separate package.

The first release contains only:

- `flow.switch`

Correlation, join, merge, and window nodes stayed deferred for the first
package release until the switch route contract was proven in real flows.

## Package Shape

`flow.switch` exposes:

- `Input`: host-registered typed input.
- `Result`: `FlowSwitchResult<TInput>`.
- `Matched`: original input when the route key is matched.
- `Default`: original input when the route key is empty or not matched.
- `Errors`: structured expression/runtime errors.

Configuration includes:

- `expression`
- `engine`
- `expressionId`
- `expressionName`
- `inputType`
- `routes`
- `defaultRoute`
- `caseSensitive`
- `emitMatchedInput`
- `emitDefaultInput`
- `boundedCapacity`

If `routes` is empty, every non-empty route key is considered matched. This
lets hosts use the result envelope and conditional links without declaring all
possible route keys up front.

## Boundary

The package does not know about any transport envelope, test runner, scenario
schema, dashboard, or product-specific correlation model.

Applications register input aliases and context factories, then decide how
`RouteKey`, `Matched`, and `DefaultRoute` are projected into their own UI or
workflow models.

## Deferred

- `flow.fork`
- `flow.merge`
- `flow.join`
- `flow.window`

`flow.correlation` was added later in `70-routing-correlation-component.md`.
Direct switch route outputs were added in `71-routing-switch-output-ports.md`.
The remaining nodes need separate contracts for buffering, timeouts, late
events, missing events, and typed result shapes.

## Verification Plan

- Run focused Routing tests.
- Run solution tests.
- Pack the Routing package.
- Resolve the package release tag from the release manifest.
