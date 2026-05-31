# Architecture Decisions

Date: 2026-05-31

## Package boundary

`FluxFlow.Engine` is a protocol-neutral workflow runtime. It knows how to build and run typed node graphs, but it does not know how to connect to brokers, call web endpoints, write files, store sessions, or render a designer.

## Extension model

Applications add behavior by registering node factories with `RuntimeNodeFactoryRegistry`.

Component packages should expose:

- one or more `IFlowNode` implementations;
- node factory registration helpers;
- component-owned options and validation;
- component-owned event type constants;
- focused tests for the component behavior.

## Definition ownership

The engine owns only graph execution definitions. Design-time metadata should remain outside the engine unless it directly affects runtime behavior.

## Scenario ownership

The base package may keep `expect.event` because it validates runtime event streams without introducing component knowledge. Any step that drives an external system belongs in a companion package.

## Versioning

Use semantic versioning. Until the public API is stable, publish prerelease versions such as `0.1.0-alpha.1`. Use `1.0.0` only after the boundary is clean, docs are accurate, and core behavior is covered by tests.
