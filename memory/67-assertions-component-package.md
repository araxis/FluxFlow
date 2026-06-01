# Assertions Component Package

Date: 2026-06-01

## Decision

`flow.assert` is now owned by `FluxFlow.Components.Assertions`, not
`FluxFlow.Components.Control`.

The control package keeps expression-driven filtering and branching:

- `flow.filter`
- `flow.when`

The assertions package owns expression-driven checks:

- `flow.assert`

## Package Shape

`flow.assert` exposes:

- `Input`: host-registered typed input.
- `Result`: `FlowAssertionResult`.
- `Passed`: original input when the assertion passes.
- `Failed`: original input when the assertion fails.
- `Errors`: structured expression/runtime errors.

The package provides:

- `FlowAssertionComponent<TInput>`
- `FlowAssertionResult`
- `FlowAssertionStatus`
- `AssertionFailure`
- assertion options with expression, input type, description, failure message,
  capacity, and routed-input controls
- host-provided expression engine and context factory registration
- package diagnostics for evaluated and failed expression paths

## Boundary

The package does not know about any consuming application shape, workspace
schema, dashboard, scenario runner, or transport envelope.

Applications register their own input aliases and context factories, then map
domain data into expression variables such as `input`, `value`, or
application-defined names.

## Compatibility Note

`FluxFlow.Components.Control` moves to `0.2.0-alpha.1` because its package
surface no longer includes `flow.assert`.

Consumers that need assertions should reference and register
`FluxFlow.Components.Assertions` alongside Control.

## Verification Plan

- Build the new source project.
- Run assertions tests.
- Run control tests to verify the removal.
- Run the mapping/control sample to verify composed package usage.
- Run solution tests before release tagging.
