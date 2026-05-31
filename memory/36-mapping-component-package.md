# Mapping Component Package

Date: 2026-05-31

## Goal

Add a reusable mapping package before recorder, dashboard, or test-oriented
packages. Mapping is a common workflow primitive and should be reusable across
host applications.

## Decisions

- Package project: `src/FluxFlow.Components.Mapping`.
- Test project: `tests/FluxFlow.Components.Mapping.Tests`.
- Package identity: `FluxFlow.Components.Mapping`.
- Package version: `0.1.0-alpha.1`.
- Initial node type: `flow.mapper`.
- Default ports are `Input` and `Output`.
- Default input and output port types are `object`.
- Hosts can register type aliases so a mapper can expose typed ports.
- Hosts provide expression engines.
- Hosts can provide per-input context factories.
- Mapping failures are per-message `FlowError` values and do not stop later
  messages.

## Contract Shape

`flow.mapper`:

- input port: `Input`
- output port: `Output`
- options: `MapperOptions`
- config fields:
  - `expression`
  - `engine`
  - `expressionId`
  - `expressionName`
  - `inputType`
  - `outputType`
  - `targetType`
  - `boundedCapacity`

`targetType` is accepted as an alias for `outputType` to make migration from
existing application definitions easier without introducing application-owned
schemas.

## Implementation Status

Implemented:

- package project and tests
- node type and port constants
- registration module and extension methods
- mapping options reader
- type alias registration
- expression engine registration and resolver support
- default and typed context factory support
- generic `FlowMapperNode<TInput, TOutput>`
- isolated runtime generic node factory
- diagnostics for success and failure
- deterministic tests for object mapping, typed mapping, per-message failure,
  diagnostics, missing expression, and missing type alias

## Deferred

- `flow.filter`
- `flow.router`
- `flow.assert`
- mapper preview helpers
- schema validation helpers
- richer output conversion helpers
