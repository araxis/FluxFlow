# Control Component Package

Date: 2026-06-01

## Goal

Add a reusable expression-driven control package after mapping. The package
should cover generic filtering, branching, and assertions without importing
host product behavior.

Update: assertion ownership was split into `FluxFlow.Components.Assertions` in
`67-assertions-component-package.md`. The current Control package owns only
`flow.filter` and `flow.when`.

## Decisions

- Package project: `src/FluxFlow.Components.Control`.
- Test project: `tests/FluxFlow.Components.Control.Tests`.
- Package identity: `FluxFlow.Components.Control`.
- Initial package version: `0.1.0-alpha.1`.
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
- initially emitted an assertion result on `Result`
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
- initial generic assertion node before the later package split
- isolated runtime generic node factory
- diagnostics for route/pass/failure behavior
- stable error codes for expression failures
- deterministic tests for filtering, branching, assertions, diagnostics, typed
  ports, missing expression, and per-message expression failure behavior

## Later Split

`flow.assert` moved to `FluxFlow.Components.Assertions` with
`FlowAssertionResult` and `AssertionFailure` contracts. Control was then moved
to `0.2.0-alpha.1` with assertion-specific ports and contracts removed.

## Deferred

- `flow.router` alias decision
- scenario-specific assertion helpers
- event/journal components
- richer assertion result metadata adapters
