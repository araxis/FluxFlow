# Control Component Package

Date: 2026-06-01

## Goal

Add a reusable expression-driven control package after mapping. The package
should cover generic filtering, branching, and assertions without importing
host product behavior.

## Decisions

- Package project: `src/FluxFlow.Components.Control`.
- Test project: `tests/FluxFlow.Components.Control.Tests`.
- Package identity: `FluxFlow.Components.Control`.
- Package version: `0.1.0-alpha.1`.
- Initial node types:
  - `flow.filter`
  - `flow.when`
  - `flow.assert`
- Default input port type is `object`.
- Hosts can register type aliases so control nodes expose typed ports.
- Hosts provide expression engines.
- Hosts can provide per-input context factories.
- Expression failures are per-message `FlowError` values and do not stop later
  messages.
- Scenario timing, journals, expected-event helpers, and isolated runtime rules
  stay outside this package.

## Contract Shape

`flow.filter`:

- input port: `Input`
- output port: `Output`
- preserves input type

`flow.when`:

- input port: `Input`
- true output port: `WhenTrue`
- false output port: `WhenFalse`
- preserves input type on both outputs

`flow.assert`:

- input port: `Input`
- result output port: `Result`
- passed output port: `Passed`
- failed output port: `Failed`
- emits `ControlAssertionResult` on `Result`
- preserves input type on `Passed` and `Failed`

Common config fields:

- `expression`
- `engine`
- `expressionId`
- `expressionName`
- `inputType`
- `boundedCapacity`

Assertion-specific optional fields:

- `name`
- `failureMessage`

## Implementation Status

Implemented:

- package project and tests
- node type and port constants
- registration module and extension methods
- expression options reader
- type alias registration
- expression engine registration and resolver support
- default and typed context factory support
- generic `FilterNode<TInput>`
- generic `WhenNode<TInput>`
- generic `AssertNode<TInput>`
- isolated runtime generic node factory
- diagnostics for route/pass/failure behavior
- stable error codes for expression failures
- deterministic tests for filtering, branching, assertions, diagnostics, typed
  ports, missing expression, and per-message expression failure behavior

## Deferred

- `flow.router` alias decision
- scenario-specific assertion helpers
- event/journal components
- richer assertion result metadata adapters
